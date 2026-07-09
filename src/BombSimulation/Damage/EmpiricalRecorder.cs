using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace BombSimulation.Damage;

/// <summary>
/// Enregistreur empirique : quand il est armé, chaque player_hurt infligé par
/// la planted_c4 devient une mesure (position de la victime au moment où
/// l'onde de choc l'atteint + dégâts bruts). Les bots reçoivent beaucoup de HP
/// pour que dmg_health ne soit pas plafonné par les 100 HP de base.
/// </summary>
public class EmpiricalRecorder
{
    private readonly PluginConfig _config;

    public bool Armed { get; private set; }
    public int SamplesThisRun { get; private set; }

    public EmpiricalRecorder(PluginConfig config)
    {
        _config = config;
    }

    public void Arm()
    {
        Armed = true;
        SamplesThisRun = 0;
        BoostBots();
    }

    public void Disarm()
    {
        Armed = false;
    }

    public bool TryRecordHurt(EventPlayerHurt ev, HeatmapData heatmap)
    {
        if (!Armed) return false;
        if (!ev.Weapon.Contains("planted_c4")) return false;

        var victim = ev.Userid;
        var pawn = victim?.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;
        if (victim == null || pawn == null || origin == null) return false;

        // dmg_health = dégâts réellement retirés ; avec les HP boostés des bots
        // c'est la valeur brute. Un humain à 100 HP qui meurt donne une mesure
        // plafonnée, on la garde quand même : 100+ = létal de toute façon.
        float damage = ev.DmgHealth;
        var armored = ev.DmgArmor > 0 || ev.Armor > 0;

        heatmap.AddSample(origin.X, origin.Y, origin.Z, damage, armored, _config.GridSpacing * 0.5f);
        SamplesThisRun++;
        return true;
    }

    /// <summary>Monte les HP des bots pour mesurer les dégâts bruts, et leur met
    /// une armure si la config demande des mesures "avec armure".</summary>
    private void BoostBots()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !player.IsBot || !player.PawnIsAlive) continue;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null) continue;

            pawn.Health = _config.RecorderBotHealth;
            pawn.MaxHealth = _config.RecorderBotHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            if (_config.RecorderBotsArmored)
            {
                pawn.ArmorValue = 100;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            }
        }
    }
}
