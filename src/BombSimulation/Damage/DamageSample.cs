namespace BombSimulation.Damage;

/// <summary>Une mesure de dégâts de bombe à une position donnée du monde.</summary>
public class DamageSample
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Dégâts bruts (HP) relevés à cette position.</summary>
    public float Damage { get; set; }

    /// <summary>La victime portait une armure au moment de la mesure.</summary>
    public bool Armored { get; set; }
}
