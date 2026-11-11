using Ian.Application.Clientes;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresClienteStore(IanDbContext context) : IClienteStore
{
    private const string IndiceTelefoneUnico = "IX_clientes_TelefoneNormalizado";

    public async Task<Cliente?> LocalizarParaCadastroAsync(
        string telefoneNormalizado,
        CancellationToken cancellationToken = default)
    {
        var local = context.Clientes.Local.FirstOrDefault(
            cliente => cliente.TelefoneNormalizado == telefoneNormalizado);
        return local ?? await LocalizarPorTelefoneAsync(telefoneNormalizado, cancellationToken);
    }

    public async Task<Cliente?> LocalizarPorTelefoneAsync(
        string telefoneNormalizado,
        CancellationToken cancellationToken = default) =>
        await context.Clientes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cliente => cliente.TelefoneNormalizado == telefoneNormalizado,
                cancellationToken);

    public async Task<IReadOnlyList<Cliente>> LocalizarPorNomeAsync(
        string nome,
        int limite,
        CancellationToken cancellationToken = default)
    {
        var padrao = $"%{EscaparLike(nome)}%";
        return await context.Clientes
            .AsNoTracking()
            .Where(cliente => EF.Functions.ILike(cliente.Nome, padrao, "\\"))
            .OrderBy(cliente => cliente.Nome)
            .ThenBy(cliente => cliente.Id)
            .Take(limite)
            .ToListAsync(cancellationToken);
    }

    public void Adicionar(Cliente cliente) => context.Clientes.Add(cliente);

    public async Task<Cliente?> SalvarNovoAsync(
        Cliente cliente,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (EhTelefoneDuplicado(exception))
        {
            context.Entry(cliente).State = EntityState.Detached;
            var existente = await LocalizarPorTelefoneAsync(
                cliente.TelefoneNormalizado,
                cancellationToken);
            if (existente is null)
            {
                throw;
            }

            return existente;
        }
    }

    private static bool EhTelefoneDuplicado(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IndiceTelefoneUnico
        };

    private static string EscaparLike(string valor) =>
        valor.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
