using Ian.Domain.Clientes;

namespace Ian.Tests;

public sealed class NormalizadorTelefoneBrasileiroTests
{
    [Theory]
    [InlineData("(11) 99999-9999")]
    [InlineData("11999999999")]
    [InlineData("+55 11 99999-9999")]
    [InlineData("5511999999999")]
    public void FormatosEquivalentesProduzemMesmoNumeroCanonico(string entrada)
    {
        var resultado = NormalizadorTelefoneBrasileiro.Normalizar(entrada);

        Assert.True(resultado.Valido);
        Assert.Equal("11999999999", resultado.TelefoneNormalizado);
        Assert.Null(resultado.Erro);
    }

    [Theory]
    [InlineData("(21) 2345-6789", "2123456789")]
    [InlineData("+55 21 2345-6789", "2123456789")]
    public void AceitaNumeroBrasileiroComDezDigitos(string entrada, string esperado)
    {
        var resultado = NormalizadorTelefoneBrasileiro.Normalizar(entrada);

        Assert.Equal(esperado, resultado.TelefoneNormalizado);
    }

    [Theory]
    [InlineData(null, ErroNormalizacaoTelefone.Vazio)]
    [InlineData("  ", ErroNormalizacaoTelefone.Vazio)]
    [InlineData("119999999", ErroNormalizacaoTelefone.TamanhoInvalido)]
    [InlineData("119999999999", ErroNormalizacaoTelefone.PrefixoInternacionalInvalido)]
    [InlineData("+54 11 99999-9999", ErroNormalizacaoTelefone.PrefixoInternacionalInvalido)]
    [InlineData("0011999999999", ErroNormalizacaoTelefone.PrefixoInternacionalInvalido)]
    [InlineData("10999999999", ErroNormalizacaoTelefone.DddInvalido)]
    [InlineData("11.99999-9999", ErroNormalizacaoTelefone.CaractereInvalido)]
    [InlineData("11A99999999", ErroNormalizacaoTelefone.CaractereInvalido)]
    [InlineData("11+999999999", ErroNormalizacaoTelefone.CaractereInvalido)]
    public void RejeitaEntradaInvalida(string? entrada, ErroNormalizacaoTelefone erroEsperado)
    {
        var resultado = NormalizadorTelefoneBrasileiro.Normalizar(entrada);

        Assert.False(resultado.Valido);
        Assert.Null(resultado.TelefoneNormalizado);
        Assert.Equal(erroEsperado, resultado.Erro);
    }
}
