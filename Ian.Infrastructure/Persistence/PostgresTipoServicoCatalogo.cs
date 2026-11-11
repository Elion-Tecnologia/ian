using Ian.Application.Catalogo;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresTipoServicoCatalogo(IanDbContext context) : ITipoServicoCatalogo
{
    public async Task<IReadOnlyList<TipoServicoCatalogoItem>> ListarAsync(
        CancellationToken cancellationToken = default) =>
        await context.TiposServico
            .AsNoTracking()
            .OrderBy(tipo => tipo.Id)
            .Select(tipo => new TipoServicoCatalogoItem(
                tipo.Id,
                tipo.Nome,
                tipo.DuracaoPadrao,
                tipo.UnidadeDuracaoPadrao,
                tipo.ModalidadePadrao))
            .ToListAsync(cancellationToken);
}
