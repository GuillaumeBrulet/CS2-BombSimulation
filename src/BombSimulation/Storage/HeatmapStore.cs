using System.Globalization;
using System.Text.Json;
using BombSimulation.Damage;

namespace BombSimulation.Storage;

/// <summary>Persistance JSON des heatmaps, par (map, position de plant arrondie).</summary>
public class HeatmapStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dataDir;
    private readonly float _rounding;

    public HeatmapStore(string moduleDirectory, float plantKeyRounding)
    {
        _dataDir = Path.Combine(moduleDirectory, "data");
        _rounding = Math.Max(1f, plantKeyRounding);
        Directory.CreateDirectory(_dataDir);
    }

    private float Round(float v) => MathF.Round(v / _rounding) * _rounding;

    private string PathFor(string map, float x, float y, float z)
    {
        var inv = CultureInfo.InvariantCulture;
        var name = string.Format(inv, "{0}_{1:0}_{2:0}_{3:0}.json", map, Round(x), Round(y), Round(z));
        return Path.Combine(_dataDir, name);
    }

    public HeatmapData LoadOrCreate(string map, float x, float y, float z)
    {
        var path = PathFor(map, x, y, z);
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<HeatmapData>(File.ReadAllText(path));
                if (loaded != null) return loaded;
            }
            catch (JsonException)
            {
                // Fichier corrompu : on repart de zéro sans écraser tant qu'on n'a pas sauvegardé.
            }
        }

        return new HeatmapData { Map = map, PlantX = x, PlantY = y, PlantZ = z };
    }

    public void Save(HeatmapData data)
    {
        var path = PathFor(data.Map, data.PlantX, data.PlantY, data.PlantZ);
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }
}
