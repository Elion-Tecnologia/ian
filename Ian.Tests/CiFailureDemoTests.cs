using Xunit;

namespace Ian.Tests;

public class CiFailureDemoTests
{
    [Fact]
    public void FailsDeliberatelyToVerifyCi()
    {
        Assert.Fail("Falha deliberada para validar a T05.");
    }
}