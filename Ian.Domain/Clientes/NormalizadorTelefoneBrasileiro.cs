using System.Text;

namespace Ian.Domain.Clientes;

public enum ErroNormalizacaoTelefone
{
    Vazio,
    CaractereInvalido,
    PrefixoInternacionalInvalido,
    TamanhoInvalido,
    DddInvalido
}

public readonly record struct ResultadoNormalizacaoTelefone(
    string? TelefoneNormalizado,
    ErroNormalizacaoTelefone? Erro)
{
    public bool Valido => TelefoneNormalizado is not null;
}

public static class NormalizadorTelefoneBrasileiro
{
    private static readonly HashSet<int> DddsValidos =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 24, 27, 28,
        31, 32, 33, 34, 35, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48, 49,
        51, 53, 54, 55,
        61, 62, 63, 64, 65, 66, 67, 68, 69,
        71, 73, 74, 75, 77, 79,
        81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 92, 93, 94, 95, 96, 97, 98, 99
    ];

    public static ResultadoNormalizacaoTelefone Normalizar(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
        {
            return Invalido(ErroNormalizacaoTelefone.Vazio);
        }

        var entrada = telefone.Trim();
        var possuiMais = entrada[0] == '+';
        var digitos = new StringBuilder(entrada.Length);

        for (var indice = 0; indice < entrada.Length; indice++)
        {
            var caractere = entrada[indice];
            if (char.IsAsciiDigit(caractere))
            {
                digitos.Append(caractere);
                continue;
            }

            if (caractere == '+' && indice == 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(caractere) || caractere is '(' or ')' or '-')
            {
                continue;
            }

            return Invalido(ErroNormalizacaoTelefone.CaractereInvalido);
        }

        var numero = digitos.ToString();
        if (possuiMais && !numero.StartsWith("55", StringComparison.Ordinal))
        {
            return Invalido(ErroNormalizacaoTelefone.PrefixoInternacionalInvalido);
        }

        if (possuiMais || numero.Length is 12 or 13)
        {
            if (!numero.StartsWith("55", StringComparison.Ordinal))
            {
                return Invalido(ErroNormalizacaoTelefone.PrefixoInternacionalInvalido);
            }

            numero = numero[2..];
        }

        if (numero.Length is not (10 or 11))
        {
            return Invalido(ErroNormalizacaoTelefone.TamanhoInvalido);
        }

        var ddd = (numero[0] - '0') * 10 + numero[1] - '0';
        if (!DddsValidos.Contains(ddd))
        {
            return Invalido(ErroNormalizacaoTelefone.DddInvalido);
        }

        return new ResultadoNormalizacaoTelefone(numero, null);
    }

    private static ResultadoNormalizacaoTelefone Invalido(ErroNormalizacaoTelefone erro) =>
        new(null, erro);
}
