using CounterStrikeSharp.API.Modules.Utils;

namespace BombSimulation.Damage;

/// <summary>
/// Ensemble des mesures de dégâts associées à un spot de plant donné.
/// Depuis le patch du 08/07/2026 les dégâts sont précalculés dans la map
/// (onde de choc bloquée par les murs), donc pour un même spot de plant le
/// résultat est déterministe : les mesures restent valables tant que la map
/// n'est pas recompilée par Valve.
/// </summary>
public class HeatmapData
{
    public string Map { get; set; } = "";
    public float PlantX { get; set; }
    public float PlantY { get; set; }
    public float PlantZ { get; set; }
    public List<DamageSample> Samples { get; set; } = new();

    /// <summary>Fenêtre verticale dans laquelle deux points sont considérés au même étage.</summary>
    public const float ZWindow = 110f;

    public void AddSample(float x, float y, float z, float damage, bool armored, float mergeRadius)
    {
        // Fusionne avec une mesure existante trop proche (on garde le pire cas).
        foreach (var s in Samples)
        {
            if (s.Armored != armored) continue;
            if (Math.Abs(s.Z - z) > ZWindow) continue;
            var dx = s.X - x;
            var dy = s.Y - y;
            if (dx * dx + dy * dy <= mergeRadius * mergeRadius)
            {
                if (damage > s.Damage) s.Damage = damage;
                return;
            }
        }

        Samples.Add(new DamageSample { X = x, Y = y, Z = z, Damage = damage, Armored = armored });
    }

    /// <summary>
    /// Estime les dégâts à une position par pondération inverse à la distance
    /// des mesures voisines. Retourne null si aucune mesure n'est assez proche.
    /// </summary>
    public float? QueryDamage(Vector pos, float searchRadius)
    {
        float weightSum = 0f;
        float valueSum = 0f;
        DamageSample? nearest = null;
        float nearestDistSq = float.MaxValue;

        foreach (var s in Samples)
        {
            if (Math.Abs(s.Z - pos.Z) > ZWindow) continue;
            var dx = s.X - pos.X;
            var dy = s.Y - pos.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq > searchRadius * searchRadius) continue;

            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = s;
            }

            var w = 1f / (distSq + 1f);
            weightSum += w;
            valueSum += w * s.Damage;
        }

        if (nearest == null) return null;

        // Tout près d'une vraie mesure : on rend la valeur exacte plutôt qu'une moyenne.
        if (nearestDistSq <= 24f * 24f) return nearest.Damage;

        return valueSum / weightSum;
    }

    /// <summary>Vrai si une mesure existe déjà près de ce point (utile pour !bombsim spread).</summary>
    public bool HasSampleNear(float x, float y, float z, float radius)
    {
        foreach (var s in Samples)
        {
            if (Math.Abs(s.Z - z) > ZWindow) continue;
            var dx = s.X - x;
            var dy = s.Y - y;
            if (dx * dx + dy * dy <= radius * radius) return true;
        }

        return false;
    }
}
