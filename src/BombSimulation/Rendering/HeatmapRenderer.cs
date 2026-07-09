using BombSimulation.Damage;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BombSimulation.Rendering;

/// <summary>
/// Dessine la heatmap au sol sous forme de croix colorées (entités beam).
/// Un vrai décal peint sur les textures n'est pas possible depuis un plugin
/// serveur ; une grille de beams posés à quelques unités du sol donne un
/// rendu très proche d'une heatmap.
/// </summary>
public class HeatmapRenderer
{
    private readonly List<CBeam> _beams = new();

    public int MarkerCount { get; private set; }

    public void Render(HeatmapData data, PluginConfig config, bool armored)
    {
        Clear();

        // Agrège les mesures par cellule de grille en gardant le pire cas,
        // puis dessine les cellules les plus proches du plant en priorité.
        var cells = new Dictionary<(int, int, int), DamageSample>();
        foreach (var s in data.Samples)
        {
            if (s.Armored != armored) continue;
            var key = (
                (int)MathF.Round(s.X / config.GridSpacing),
                (int)MathF.Round(s.Y / config.GridSpacing),
                (int)MathF.Round(s.Z / HeatmapData.ZWindow));
            if (!cells.TryGetValue(key, out var existing) || s.Damage > existing.Damage)
                cells[key] = s;
        }

        var ordered = cells.Values
            .OrderBy(s =>
            {
                var dx = s.X - data.PlantX;
                var dy = s.Y - data.PlantY;
                return dx * dx + dy * dy;
            })
            .Take(config.MaxMarkers);

        foreach (var s in ordered)
            DrawCross(s, config);

        DrawPlantMarker(data, config);
    }

    private void DrawCross(DamageSample s, PluginConfig config)
    {
        var half = config.MarkerSize / 2f;
        var z = s.Z + 4f;
        var color = DamagePalette.ForDamage(s.Damage);

        if (DrawBeam(new Vector(s.X - half, s.Y - half, z), new Vector(s.X + half, s.Y + half, z), config.BeamWidth, color)
            & DrawBeam(new Vector(s.X - half, s.Y + half, z), new Vector(s.X + half, s.Y - half, z), config.BeamWidth, color))
        {
            MarkerCount++;
        }
    }

    private void DrawPlantMarker(HeatmapData data, PluginConfig config)
    {
        DrawBeam(
            new Vector(data.PlantX, data.PlantY, data.PlantZ),
            new Vector(data.PlantX, data.PlantY, data.PlantZ + 96f),
            config.BeamWidth * 2f,
            System.Drawing.Color.White);
    }

    private bool DrawBeam(Vector start, Vector end, float width, System.Drawing.Color color)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam == null || !beam.IsValid) return false;

        beam.Render = color;
        beam.Width = width;
        beam.Teleport(start, new QAngle(), new Vector());
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.DispatchSpawn();
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        _beams.Add(beam);
        return true;
    }

    public void Clear()
    {
        foreach (var beam in _beams)
        {
            if (beam.IsValid) beam.Remove();
        }

        _beams.Clear();
        MarkerCount = 0;
    }

    /// <summary>À appeler au changement de map : les entités n'existent plus.</summary>
    public void Reset()
    {
        _beams.Clear();
        MarkerCount = 0;
    }
}
