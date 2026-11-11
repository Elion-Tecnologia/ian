using Ian.Domain.Entities;

namespace Ian.Application.Catalogo;

public interface ITipoServicoCatalogo
{
    Task<IReadOnlyList<TipoServicoCatalogoItem>> ListarAsync(
        CancellationToken cancellationToken = default);
}

public sealed record TipoServicoCatalogoItem(
    long Id,
    string Nome,
    int DuracaoPadrao,
    UnidadeDuracao UnidadeDuracaoPadrao,
    ModalidadeServico ModalidadePadrao);
