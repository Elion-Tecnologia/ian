using Ian.Domain.Entities;

namespace Ian.Application.Clientes;

public sealed record CadastrarClienteSolicitacao(
    string? Nome,
    string? Telefone,
    string? EnderecoPrincipal = null);

public sealed record ClienteEncontrado(
    long Id,
    string Nome,
    string TelefoneNormalizado,
    string? EnderecoPrincipal);

public enum ErroCliente
{
    NomeObrigatorio,
    TelefoneInvalido,
    NomeBuscaObrigatorio
}

public enum EstadoPreparacaoCliente
{
    Novo,
    Existente,
    Invalido
}

public sealed record PreparacaoCadastroCliente(
    EstadoPreparacaoCliente Estado,
    Cliente? Cliente,
    ErroCliente? Erro);

public enum EstadoCadastroCliente
{
    Cadastrado,
    Existente,
    Invalido
}

public sealed record ResultadoCadastroCliente(
    EstadoCadastroCliente Estado,
    ClienteEncontrado? Cliente,
    ErroCliente? Erro);

public enum EstadoLocalizacaoCliente
{
    Nenhum,
    Unico,
    Ambiguo,
    Invalido
}

public sealed record ResultadoLocalizacaoCliente(
    EstadoLocalizacaoCliente Estado,
    IReadOnlyList<ClienteEncontrado> Clientes,
    bool MaisResultados,
    ErroCliente? Erro);

public interface IClientes
{
    Task<ResultadoLocalizacaoCliente> LocalizarPorIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PreparacaoCadastroCliente> PrepararCadastroAsync(
        CadastrarClienteSolicitacao solicitacao,
        CancellationToken cancellationToken = default);

    Task<ResultadoCadastroCliente> CadastrarAsync(
        CadastrarClienteSolicitacao solicitacao,
        CancellationToken cancellationToken = default);

    Task<ResultadoLocalizacaoCliente> LocalizarPorTelefoneAsync(
        string? telefone,
        CancellationToken cancellationToken = default);

    Task<ResultadoLocalizacaoCliente> LocalizarPorNomeAsync(
        string? nome,
        CancellationToken cancellationToken = default);
}

public interface IClienteStore
{
    Task<Cliente?> LocalizarPorIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Cliente?> LocalizarParaCadastroAsync(
        string telefoneNormalizado,
        CancellationToken cancellationToken = default);

    Task<Cliente?> LocalizarPorTelefoneAsync(
        string telefoneNormalizado,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Cliente>> LocalizarPorNomeAsync(
        string nome,
        int limite,
        CancellationToken cancellationToken = default);

    void Adicionar(Cliente cliente);

    Task<Cliente?> SalvarNovoAsync(
        Cliente cliente,
        CancellationToken cancellationToken = default);
}
