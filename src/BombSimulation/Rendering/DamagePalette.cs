using System.Drawing;

namespace BombSimulation.Rendering;

/// <summary>Dégradé dégâts → couleur : vert → jaune → orange → rouge → violet (létal).</summary>
public static class DamagePalette
{
    private static readonly (float Damage, Color Color)[] Stops =
    {
        (0f, Color.FromArgb(0, 180, 0)),
        (25f, Color.FromArgb(180, 220, 0)),
        (50f, Color.FromArgb(255, 160, 0)),
        (80f, Color.FromArgb(255, 40, 40)),
        (100f, Color.FromArgb(170, 0, 255)),
    };

    public static Color ForDamage(float damage)
    {
        if (damage <= Stops[0].Damage) return Stops[0].Color;
        if (damage >= Stops[^1].Damage) return Stops[^1].Color;

        for (var i = 1; i < Stops.Length; i++)
        {
            if (damage > Stops[i].Damage) continue;
            var (d0, c0) = Stops[i - 1];
            var (d1, c1) = Stops[i];
            var t = (damage - d0) / (d1 - d0);
            return Color.FromArgb(
                (int)(c0.R + (c1.R - c0.R) * t),
                (int)(c0.G + (c1.G - c0.G) * t),
                (int)(c0.B + (c1.B - c0.B) * t));
        }

        return Stops[^1].Color;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
