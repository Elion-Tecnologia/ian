using Ian.Domain.Clientes;
using Ian.Domain.Entities;

namespace Ian.Application.Clientes;

public sealed class ClientesApplication(IClienteStore store) : IClientes
{
    private const int LimiteBuscaNome = 10;

    public async Task<PreparacaoCadastroCliente> PrepararCadastroAsync(
        CadastrarClienteSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);

        var nome = NormalizarEspacos(solicitacao.Nome);
        if (nome is null)
        {
            return new(EstadoPreparacaoCliente.Invalido, null, ErroCliente.NomeObrigatorio);
        }

        var telefone = NormalizadorTelefoneBrasileiro.Normalizar(solicitacao.Telefone);
        if (telefone.TelefoneNormalizado is not { } telefoneNormalizado)
        {
            return new(EstadoPreparacaoCliente.Invalido, null, ErroCliente.TelefoneInvalido);
        }

        var existente = await store.LocalizarParaCadastroAsync(
            telefoneNormalizado,
            cancellationToken);
        if (existente is not null)
        {
            var estado = existente.Id == 0
                ? EstadoPreparacaoCliente.Novo
                : EstadoPreparacaoCliente.Existente;
            return new(estado, existente, null);
        }

        var cliente = new Cliente
        {
            Nome = nome,
            TelefoneNormalizado = telefoneNormalizado,
            EnderecoPrincipal = NormalizarEspacos(solicitacao.EnderecoPrincipal)
        };
        store.Adicionar(cliente);
        return new(EstadoPreparacaoCliente.Novo, cliente, null);
    }

    public async Task<ResultadoCadastroCliente> CadastrarAsync(
        CadastrarClienteSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        var preparacao = await PrepararCadastroAsync(solicitacao, cancellationToken);
        if (preparacao.Estado == EstadoPreparacaoCliente.Invalido)
        {
            return new(EstadoCadastroCliente.Invalido, null, preparacao.Erro);
        }

        if (preparacao.Cliente is not { } cliente)
        {
            throw new InvalidOperationException("A preparação válida deve fornecer o cliente.");
        }

        if (preparacao.Estado == EstadoPreparacaoCliente.Existente)
        {
            return new(EstadoCadastroCliente.Existente, Projetar(cliente), null);
        }

        var existente = await store.SalvarNovoAsync(cliente, cancellationToken);
        return existente is null
            ? new(EstadoCadastroCliente.Cadastrado, Projetar(cliente), null)
            : new(EstadoCadastroCliente.Existente, Projetar(existente), null);
    }

    public async Task<ResultadoLocalizacaoCliente> LocalizarPorTelefoneAsync(
        string? telefone,
        CancellationToken cancellationToken = default)
    {
        var normalizacao = NormalizadorTelefoneBrasileiro.Normalizar(telefone);
        if (normalizacao.TelefoneNormalizado is not { } telefoneNormalizado)
        {
            return Invalido(ErroCliente.TelefoneInvalido);
        }

        var cliente = await store.LocalizarPorTelefoneAsync(telefoneNormalizado, cancellationToken);
        return cliente is null
            ? Nenhum()
            : new(EstadoLocalizacaoCliente.Unico, [Projetar(cliente)], false, null);
    }

    public async Task<ResultadoLocalizacaoCliente> LocalizarPorNomeAsync(
        string? nome,
        CancellationToken cancellationToken = default)
    {
        var busca = NormalizarEspacos(nome);
        if (busca is null)
        {
            return Invalido(ErroCliente.NomeBuscaObrigatorio);
        }

        var encontrados = (await store.LocalizarPorNomeAsync(
            busca,
            LimiteBuscaNome + 1,
            cancellationToken)).ToList();
        var maisResultados = encontrados.Count > LimiteBuscaNome;
        if (maisResultados)
        {
            encontrados.RemoveAt(encontrados.Count - 1);
        }

        var resultado = encontrados.Select(Projetar).ToArray();
        return resultado.Length switch
        {
            0 => Nenhum(),
            1 => new(EstadoLocalizacaoCliente.Unico, resultado, maisResultados, null),
            _ => new(EstadoLocalizacaoCliente.Ambiguo, resultado, maisResultados, null)
        };
    }

    private static string? NormalizarEspacos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        return string.Join(' ', valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ClienteEncontrado Projetar(Cliente cliente) =>
        new(cliente.Id, cliente.Nome, cliente.TelefoneNormalizado, cliente.EnderecoPrincipal);

    private static ResultadoLocalizacaoCliente Nenhum() =>
        new(EstadoLocalizacaoCliente.Nenhum, Array.Empty<ClienteEncontrado>(), false, null);

    private static ResultadoLocalizacaoCliente Invalido(ErroCliente erro) =>
        new(EstadoLocalizacaoCliente.Invalido, Array.Empty<ClienteEncontrado>(), false, erro);
}
