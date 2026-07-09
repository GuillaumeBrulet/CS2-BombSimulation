using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace BombSimulation.Damage;

/// <summary>
/// Enregistreur empirique : quand il est armé, chaque player_hurt infligé par
/// la planted_c4 devient une mesure (position de la victime au moment où
/// l'onde de choc l'atteint + dégâts bruts). Les bots reçoivent beaucoup de HP
/// pour que dmg_health ne soit pas plafonné par les 100 HP de base.
/// Les bots placés mais jamais touchés donnent une mesure explicite à 0 :
/// la case est "safe" et ne sera plus jamais re-testée (convergence rapide).
/// </summary>
public class EmpiricalRecorder
{
    private readonly PluginConfig _config;

    /// <summary>Cases visées par le placement de cette vague : slot du bot → position cible.</summary>
    private readonly Dictionary<int, (float X, float Y, float Z)> _placements = new();
    private readonly HashSet<int> _hurtSlots = new();

    public bool Armed { get; private set; }

    /// <summary>Mesures issues d'un player_hurt (dégâts &gt; 0) sur cette vague.</summary>
    public int HurtSamplesThisRun { get; private set; }

    /// <summary>Mesures à 0 ajoutées pour les bots placés mais épargnés.</summary>
    public int ZeroSamplesThisRun { get; private set; }

    public int SamplesThisRun => HurtSamplesThisRun + ZeroSamplesThisRun;

    /// <summary>Heure (Server.CurrentTime) du dernier player_hurt capturé —
    /// sert à clore la fenêtre de capture dès que l'onde a fini de frapper.</summary>
    public float LastHurtTime { get; private set; }

    public EmpiricalRecorder(PluginConfig config)
    {
        _config = config;
    }

    public void Arm()
    {
        Armed = true;
        HurtSamplesThisRun = 0;
        ZeroSamplesThisRun = 0;
        LastHurtTime = 0f;
        _placements.Clear();
        _hurtSlots.Clear();
        BoostBots();
    }

    public void Disarm()
    {
        Armed = false;
    }

    /// <summary>Mémorise la case visée pour un bot téléporté par le spread :
    /// s'il n'est pas touché par l'onde, la case sera enregistrée à 0 dégât.</summary>
    public void RegisterPlacement(int slot, float x, float y, float z)
    {
        if (Armed) _placements[slot] = (x, y, z);
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
        HurtSamplesThisRun++;
        _hurtSlots.Add(victim.Slot);
        LastHurtTime = Server.CurrentTime;
        return true;
    }

    /// <summary>Enregistre 0 dégât pour chaque bot placé sur une case et jamais
    /// touché par l'onde : la case est hors de portée (murs, dissipation) et ne
    /// sera plus re-testée. À n'appeler que si la détonation a bien eu lieu
    /// (au moins un player_hurt quelque part), sinon les zéros seraient faux.</summary>
    public int RecordZeroesForUnhurt(HeatmapData heatmap)
    {
        if (!Armed) return 0;

        var added = 0;
        foreach (var (slot, target) in _placements)
        {
            if (_hurtSlots.Contains(slot)) continue;

            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            var origin = pawn?.AbsOrigin;
            if (player == null || !player.IsValid || !player.PawnIsAlive || origin == null) continue;

            // Le bot doit être resté sur sa case : s'il a bougé (chute, poussée),
            // un 0 ne décrirait pas la case visée — on la laisse non mesurée.
            var maxDrift = _config.GridSpacing * 0.75f;
            if (Math.Abs(origin.X - target.X) > maxDrift || Math.Abs(origin.Y - target.Y) > maxDrift) continue;

            heatmap.AddSample(target.X, target.Y, origin.Z, 0f, _config.RecorderBotsArmored, _config.GridSpacing * 0.5f);
            ZeroSamplesThisRun++;
            added++;
        }

        return added;
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
