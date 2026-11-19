using MiniplayerBarra.Core;
using Xunit;

namespace MiniplayerBarra.Tests;

public class VolumeStepTests
{
    [Theory]
    [InlineData(0.50f, 1, 0.55f)]
    [InlineData(0.50f, -1, 0.45f)]
    [InlineData(0.50f, 2, 0.60f)]
    [InlineData(1.00f, 1, 1.00f)]
    [InlineData(0.00f, -1, 0.00f)]
    [InlineData(0.37f, 1, 0.40f)]   // volume fora da grade: encaixa em 5% e anda
    [InlineData(0.42f, -1, 0.35f)]
    public void Apply_moves_in_5_percent_steps(float current, int notches, float expected) =>
        Assert.Equal(expected, VolumeStep.Apply(current, notches), 3);

    [Theory]
    [InlineData(0.55f, 55)]
    [InlineData(0f, 0)]
    [InlineData(1f, 100)]
    public void Percent_rounds(float v, int expected) => Assert.Equal(expected, VolumeStep.Percent(v));
}
