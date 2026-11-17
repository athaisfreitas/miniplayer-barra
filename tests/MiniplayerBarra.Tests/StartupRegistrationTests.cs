using MiniplayerBarra;

namespace MiniplayerBarra.Tests;

public class StartupRegistrationTests
{
    private const string Local = @"C:\Users\someone\AppData\Local";

    [Fact]
    public void InstalledPath_Registers() =>
        Assert.True(StartupRegistration.ShouldRegister(Local + @"\Programs\MiniplayerBarra\MiniplayerBarra.exe", Local));

    [Fact]
    public void DebugBuildPath_DoesNotRegister() =>
        Assert.False(StartupRegistration.ShouldRegister(@"C:\src\miniplayer-barra\src\MiniplayerBarra\bin\Debug\net8.0\MiniplayerBarra.exe", Local));

    [Fact]
    public void CasingAndTrailingSlashOnLocalAppData_Match() =>
        Assert.True(StartupRegistration.ShouldRegister(
            (Local + @"\programs\MINIPLAYERBARRA\miniplayerbarra.EXE"), Local.ToUpperInvariant() + @"\"));

    [Fact]
    public void SiblingDirWithSimilarName_DoesNotRegister() =>
        Assert.False(StartupRegistration.ShouldRegister(Local + @"\Programs\MiniplayerBarra2\MiniplayerBarra.exe", Local));

    [Fact]
    public void SubfolderOfInstallDir_DoesNotRegister() =>
        Assert.False(StartupRegistration.ShouldRegister(Local + @"\Programs\MiniplayerBarra\sub\MiniplayerBarra.exe", Local));
}
