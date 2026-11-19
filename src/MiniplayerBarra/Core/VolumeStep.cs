namespace MiniplayerBarra.Core;

/// <summary>Volume em degraus de 5% (20 degraus de 0 a 1).</summary>
public static class VolumeStep
{
    private const int Steps = 20;

    /// <param name="notches">Cliques da rodinha: positivo sobe, negativo desce.</param>
    public static float Apply(float current, int notches)
    {
        int step = (int)MathF.Round(Math.Clamp(current, 0f, 1f) * Steps) + notches;
        return Math.Clamp(step, 0, Steps) / (float)Steps;
    }

    public static int Percent(float volume) => (int)MathF.Round(volume * 100f);
}
