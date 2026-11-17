using System.Drawing;
using MiniplayerBarra.Ui;

namespace MiniplayerBarra.Tests;

public class ThemeTests
{
    [Fact]
    public void FromRegistryValue_0_WhiteText() => Assert.Equal(Color.White.ToArgb(), Theme.FromRegistryValue(0).Text.ToArgb());

    [Fact]
    public void FromRegistryValue_1_BlackText() => Assert.Equal(Color.Black.ToArgb(), Theme.FromRegistryValue(1).Text.ToArgb());

    [Fact]
    public void FromRegistryValue_null_WhiteText() => Assert.Equal(Color.White.ToArgb(), Theme.FromRegistryValue(null).Text.ToArgb());

    [Fact]
    public void Accent_from_abgr_dword() =>
        Assert.Equal(Color.FromArgb(0x00, 0x78, 0xD4).ToArgb(), Theme.AccentFromAbgr(unchecked((int)0xFFD47800)).ToArgb());
}
