using Ian.Application.Clientes;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class ClientesPostgresTests
{
    [PostgresFact]
    public async Task CadastraClienteComTelefoneCanonicoEEnderecoOpcional()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var resultado = await clientes.CadastrarAsync(
            new("  Maria   Silva ", "+55 (11) 99999-9999"));

        Assert.Equal(EstadoCadastroCliente.Cadastrado, resultado.Estado);
        Assert.NotNull(resultado.Cliente);
        Assert.Equal("Maria Silva", resultado.Cliente.Nome);
        Assert.Equal("11999999999", resultado.Cliente.TelefoneNormalizado);
        Assert.Null(resultado.Cliente.EnderecoPrincipal);
        Assert.Equal(1, await db.Clientes.CountAsync());
    }

    [PostgresFact]
    public async Task ReutilizaClienteExistenteSemAlterarNomeOuEndereco()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);
        var primeiro = await clientes.CadastrarAsync(
            new("Maria Silva", "11999999999", "Rua Original, 10"));

        var repetido = await clientes.CadastrarAsync(
            new("Maria", "(11) 99999-9999", "Outro endereço"));

        Assert.Equal(EstadoCadastroCliente.Existente, repetido.Estado);
        Assert.Equal(primeiro.Cliente?.Id, repetido.Cliente?.Id);
        Assert.Equal("Maria Silva", repetido.Cliente?.Nome);
        Assert.Equal("Rua Original, 10", repetido.Cliente?.EnderecoPrincipal);
        Assert.Equal(1, await db.Clientes.CountAsync());
    }

    [PostgresFact]
    public async Task DuasTentativasConcorrentesReutilizamMesmoCliente()
    {
        await InitializeAsync();
        await using var firstDb = PostgresTestDatabase.CreateContext();
        await using var secondDb = PostgresTestDatabase.CreateContext();
        var first = CreateClientes(firstDb);
        var second = CreateClientes(secondDb);

        var resultados = await Task.WhenAll(
            first.CadastrarAsync(new("Cliente original", "11988887777")),
            second.CadastrarAsync(new("Cliente concorrente", "+55 11 98888-7777")));

        Assert.Single(resultados, item => item.Estado == EstadoCadastroCliente.Cadastrado);
        Assert.Single(resultados, item => item.Estado == EstadoCadastroCliente.Existente);
        Assert.Equal(resultados[0].Cliente?.Id, resultados[1].Cliente?.Id);
        var vencedor = Assert.Single(
            resultados,
            item => item.Estado == EstadoCadastroCliente.Cadastrado);

        await using var verification = PostgresTestDatabase.CreateContext();
        var persisted = await verification.Clientes.AsNoTracking().SingleAsync();
        Assert.Equal(vencedor.Cliente?.Nome, persisted.Nome);
        Assert.Equal("11988887777", persisted.TelefoneNormalizado);
    }

    [PostgresFact]
    public async Task ValidaNomeETelefoneAntesDePersistir()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var semNome = await clientes.CadastrarAsync(new(" ", "11999999999"));
        var telefoneInvalido = await clientes.CadastrarAsync(new("Maria", "telefone"));

        Assert.Equal(ErroCliente.NomeObrigatorio, semNome.Erro);
        Assert.Equal(ErroCliente.TelefoneInvalido, telefoneInvalido.Erro);
        Assert.Empty(await db.Clientes.AsNoTracking().ToListAsync());
    }

    [PostgresFact]
    public async Task PreparacaoAdicionaAoContextoSemGravarAntecipadamente()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var resultado = await clientes.PrepararCadastroAsync(
            new("Cliente transacional", "21988887777"));

        Assert.Equal(EstadoPreparacaoCliente.Novo, resultado.Estado);
        Assert.NotNull(resultado.Cliente);
        Assert.Equal(EntityState.Added, db.Entry(resultado.Cliente).State);
        await using var verification = PostgresTestDatabase.CreateContext();
        Assert.Equal(0, await verification.Clientes.CountAsync());
    }

    [PostgresFact]
    public async Task LocalizaPorTelefoneSemRastreamento()
    {
        await InitializeAsync();
        await SeedAsync(
            new Cliente
            {
                Nome = "Maria Silva",
                TelefoneNormalizado = "11999999999",
                EnderecoPrincipal = "Rua A"
            });
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var encontrado = await clientes.LocalizarPorTelefoneAsync("+55 (11) 99999-9999");
        var ausente = await clientes.LocalizarPorTelefoneAsync("21999999999");

        Assert.Equal(EstadoLocalizacaoCliente.Unico, encontrado.Estado);
        Assert.Equal("Maria Silva", Assert.Single(encontrado.Clientes).Nome);
        Assert.Equal(EstadoLocalizacaoCliente.Nenhum, ausente.Estado);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task BuscaPorNomeIgnoraCaixaENormalizaEspacos()
    {
        await InitializeAsync();
        await SeedAsync(
            new Cliente { Nome = "Ana Maria", TelefoneNormalizado = "11911111111" },
            new Cliente { Nome = "ANA MARIA Souza", TelefoneNormalizado = "11922222222" });
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var resultado = await clientes.LocalizarPorNomeAsync("  ana   maria ");
        var repeticao = await clientes.LocalizarPorNomeAsync("ANA MARIA");
        var unico = await clientes.LocalizarPorNomeAsync("Souza");

        Assert.Equal(EstadoLocalizacaoCliente.Ambiguo, resultado.Estado);
        Assert.Equal(2, resultado.Clientes.Count);
        Assert.Contains(resultado.Clientes, item => item.Nome == "Ana Maria");
        Assert.Contains(resultado.Clientes, item => item.Nome == "ANA MARIA Souza");
        Assert.Equal(
            resultado.Clientes.Select(item => item.Id),
            repeticao.Clientes.Select(item => item.Id));
        Assert.False(resultado.MaisResultados);
        Assert.Equal(EstadoLocalizacaoCliente.Unico, unico.Estado);
        Assert.Equal("ANA MARIA Souza", Assert.Single(unico.Clientes).Nome);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task BuscaPorNomeEscapaCuringasELimitaResultados()
    {
        await InitializeAsync();
        var seeded = Enumerable.Range(1, 12)
            .Select(index => new Cliente
            {
                Nome = $"Cliente % especial {index:00}",
                TelefoneNormalizado = $"1197777{index:0000}"
            })
            .Append(new Cliente { Nome = "Cliente comum", TelefoneNormalizado = "11966666666" })
            .ToArray();
        await SeedAsync(seeded);
        await using var db = PostgresTestDatabase.CreateContext();
        var clientes = CreateClientes(db);

        var resultado = await clientes.LocalizarPorNomeAsync("% especial");
        var sublinhadoAusente = await clientes.LocalizarPorNomeAsync("_");

        Assert.Equal(EstadoLocalizacaoCliente.Ambiguo, resultado.Estado);
        Assert.Equal(10, resultado.Clientes.Count);
        Assert.True(resultado.MaisResultados);
        Assert.Equal(EstadoLocalizacaoCliente.Nenhum, sublinhadoAusente.Estado);
    }

    [PostgresFact]
    public async Task FalhaDeBancoNaoEhInterpretadaComoClienteExistente()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        db.TiposServico.Add(new TipoServico
        {
            Nome = TiposServicoPredefinidos.VisitaTecnicaNome,
            DuracaoPadrao = 15,
            UnidadeDuracaoPadrao = UnidadeDuracao.Minutos,
            ModalidadePadrao = ModalidadeServico.Interno
        });
        var clientes = CreateClientes(db);

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() =>
            clientes.CadastrarAsync(new("Maria", "11999999999")));
        var postgres = Assert.IsType<PostgresException>(erro.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.NotEqual("IX_clientes_TelefoneNormalizado", postgres.ConstraintName);
    }

    private static async Task InitializeAsync()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
    }

    private static ClientesApplication CreateClientes(IanDbContext db) =>
        new(new PostgresClienteStore(db));

    private static async Task SeedAsync(params Cliente[] clientes)
    {
        await using var db = PostgresTestDatabase.CreateContext();
        db.Clientes.AddRange(clientes);
        await db.SaveChangesAsync();
    }
}
