using BombSimulation.Damage;
using BombSimulation.Rendering;
using BombSimulation.Storage;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace BombSimulation;

public class BombSimulationPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "CS2 Bomb Simulation";
    public override string ModuleVersion => "0.2.0";
    public override string ModuleAuthor => "GuillaumeBrulet";

    public override string ModuleDescription =>
        "Simule l'onde de choc de la bombe (patch 08/07/2026) et affiche les dégâts au sol en heatmap colorée.";

    public PluginConfig Config { get; set; } = new();

    private HeatmapStore? _store;
    private EmpiricalRecorder? _recorder;
    private PredictedDamageReader? _reader;
    private readonly HeatmapRenderer _renderer = new();

    private Vector? _plantPos;
    private HeatmapData? _heatmap;
    private int _plantSite;

    // Mode auto : campagne de vagues d'explosions enchaînées sans intervention.
    private bool _autoMode;
    private bool _campaignRunning;
    private int _wavesLeft;

    private readonly HashSet<int> _hudSlots = new();
    private readonly Dictionary<int, string> _hudCache = new();

    private string Prefix => $" {ChatColors.Green}[BombSim]{ChatColors.Default}";

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        _store = new HeatmapStore(ModuleDirectory, Config.PlantKeyRounding);
        _recorder = new EmpiricalRecorder(Config);
        _reader = new PredictedDamageReader(Config.PredictedDamageSchemaClass, Config.PredictedDamageSchemaField);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    private void OnMapStart(string mapName)
    {
        _plantPos = null;
        _heatmap = null;
        _renderer.Reset();
        _recorder?.Disarm();
        _hudSlots.Clear();
        _hudCache.Clear();
    }

    // ---------------------------------------------------------------- events

    [GameEventHandler]
    public HookResult OnBombPlanted(EventBombPlanted ev, GameEventInfo info)
    {
        var site = ev.Site;

        // La planted_c4 n'existe pas encore au moment de l'événement.
        Server.NextFrame(() =>
        {
            var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            var origin = c4?.AbsOrigin;
            if (origin == null || _store == null) return;

            _plantPos = new Vector(origin.X, origin.Y, origin.Z);
            _plantSite = site;
            _heatmap = _store.LoadOrCreate(Server.MapName, origin.X, origin.Y, origin.Z);

            if (_autoMode && !_campaignRunning)
            {
                StartCampaign();
                return;
            }

            var count = _heatmap.Samples.Count;
            Server.PrintToChatAll(count > 0
                ? $"{Prefix} Bombe posée — {ChatColors.Yellow}{count}{ChatColors.Default} mesures connues pour ce spot. {ChatColors.Yellow}!bombsim show{ChatColors.Default} pour afficher, {ChatColors.Yellow}!bombsim record{ChatColors.Default} pour en ajouter."
                : $"{Prefix} Bombe posée — nouveau spot, aucune mesure. Lance {ChatColors.Yellow}!bombsim record{ChatColors.Default} puis laisse exploser.");
        });

        return HookResult.Continue;
    }

    // ------------------------------------------------------------ mode auto

    private void StartCampaign()
    {
        if (_plantPos == null || _heatmap == null) return;

        _campaignRunning = true;
        _wavesLeft = Config.AutoMaxWaves;
        Server.PrintToChatAll($"{Prefix} {ChatColors.Green}Campagne auto lancée{ChatColors.Default} : jusqu'à {Config.AutoMaxWaves} vagues d'explosion, du plus proche au plus loin. Ne touche à rien !");
        RunWave();
    }

    private void RunWave()
    {
        if (!_campaignRunning || _plantPos == null || _heatmap == null) return;

        _recorder?.Arm();
        var moved = SpreadBots(_plantPos, _heatmap);

        if (moved == 0)
        {
            FinishCampaign("toutes les cases à portée sont mesurées");
            return;
        }

        var wave = Config.AutoMaxWaves - _wavesLeft + 1;
        Server.PrintToChatAll($"{Prefix} Vague {wave} : {moved} bots placés, détonation…");
        AddTimer(Config.AutoSpreadSettleSeconds, DetonateForCampaign);
    }

    private void DetonateForCampaign()
    {
        if (!_campaignRunning || _plantPos == null) return;

        var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
        if (c4 == null || !c4.IsValid)
        {
            // Plus de bombe (vagues suivantes) : on en replante une nous-mêmes.
            c4 = SpawnPlantedC4(_plantPos, _plantSite);
            if (c4 == null)
            {
                FinishCampaign("impossible de replanter la bombe automatiquement — replante à la main et relance");
                return;
            }
        }

        c4.C4Blow = Server.CurrentTime + 1f;
        Utilities.SetStateChanged(c4, "CPlantedC4", "m_flC4Blow");
    }

    private CPlantedC4? SpawnPlantedC4(Vector pos, int site)
    {
        var c4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
        if (c4 == null || !c4.IsValid) return null;

        c4.Teleport(pos, new QAngle(), new Vector());
        c4.DispatchSpawn();

        c4.BombTicking = true;
        c4.TimerLength = 2f;
        c4.C4Blow = Server.CurrentTime + 2f;
        c4.BombSite = site;
        Utilities.SetStateChanged(c4, "CPlantedC4", "m_bBombTicking");
        Utilities.SetStateChanged(c4, "CPlantedC4", "m_flC4Blow");
        Utilities.SetStateChanged(c4, "CPlantedC4", "m_nBombSite");

        return c4;
    }

    private void FinishCampaign(string reason)
    {
        _campaignRunning = false;

        if (_heatmap != null)
        {
            _store?.Save(_heatmap);
            _renderer.Render(_heatmap, Config, Config.RecorderBotsArmored);
        }

        _recorder?.Disarm();
        Server.PrintToChatAll($"{Prefix} {ChatColors.Green}Campagne terminée{ChatColors.Default} ({reason}) — {_heatmap?.Samples.Count ?? 0} mesures au total. {ChatColors.Yellow}!dmg{ChatColors.Default} pour explorer.");
    }

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt ev, GameEventInfo info)
    {
        if (_recorder is { Armed: true } && _heatmap != null)
            _recorder.TryRecordHurt(ev, _heatmap);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBombExploded(EventBombExploded ev, GameEventInfo info)
    {
        if (_recorder is { Armed: true })
        {
            // L'onde de choc continue de se propager après l'explosion :
            // on laisse la fenêtre de capture ouverte quelques secondes.
            AddTimer(Config.RecordWindowSeconds, FinalizeRecording);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart ev, GameEventInfo info)
    {
        // Le restart de round détruit les beams : on oublie nos références.
        _renderer.Reset();
        return HookResult.Continue;
    }

    private void FinalizeRecording()
    {
        if (_recorder is not { Armed: true } || _heatmap == null || _store == null) return;

        var added = _recorder.SamplesThisRun;
        _recorder.Disarm();
        _store.Save(_heatmap);
        _renderer.Render(_heatmap, Config, Config.RecorderBotsArmored);

        if (_campaignRunning)
        {
            _wavesLeft--;

            if (added == 0)
            {
                FinishCampaign("aucune mesure capturée sur la dernière vague — la détonation automatique ne fonctionne peut-être pas sur cette map");
                return;
            }

            if (_wavesLeft <= 0)
            {
                FinishCampaign("nombre maximum de vagues atteint");
                return;
            }

            Server.PrintToChatAll($"{Prefix} +{added} mesures ({_heatmap.Samples.Count} au total). Vague suivante dans {Config.AutoRespawnDelaySeconds:0}s…");
            AddTimer(Config.AutoRespawnDelaySeconds, RunWave);
            return;
        }

        Server.PrintToChatAll(
            $"{Prefix} Enregistrement terminé : {ChatColors.Yellow}{added}{ChatColors.Default} nouvelles mesures " +
            $"({_heatmap.Samples.Count} au total pour ce spot). Replante au même endroit et recommence pour densifier.");
    }

    // -------------------------------------------------------------- commands

    [ConsoleCommand("css_bombsim", "Simulation de l'explosion de la bombe")]
    [CommandHelper(minArgs: 0, usage: "[show|record|spread|boom|clear|status|legend]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBombSimCommand(CCSPlayerController? player, CommandInfo command)
    {
        var sub = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "help";

        switch (sub)
        {
            case "prac":
                CmdPrac(player);
                break;
            case "auto":
                CmdAuto(player);
                break;
            case "stop":
                if (_campaignRunning) FinishCampaign("arrêt manuel");
                _autoMode = false;
                Reply(player, $"{Prefix} Mode auto désactivé.");
                break;
            case "c4":
                CmdGiveC4(player);
                break;
            case "show":
                CmdShow(player, armored: command.ArgCount > 2 && command.GetArg(2).StartsWith("arm"));
                break;
            case "record":
                CmdRecord(player);
                break;
            case "spread":
                CmdSpread(player);
                break;
            case "boom":
                CmdBoom(player);
                break;
            case "clear":
                _renderer.Clear();
                Reply(player, $"{Prefix} Affichage effacé.");
                break;
            case "status":
                CmdStatus(player);
                break;
            case "legend":
                CmdLegend(player);
                break;
            default:
                Reply(player, $"{Prefix} Usage : {ChatColors.Yellow}!bombsim prac | auto | stop | c4 | show [armored] | record | spread | boom | clear | status | legend");
                Reply(player, $"{Prefix} Et {ChatColors.Yellow}!dmg{ChatColors.Default} (HUD dégâts) / {ChatColors.Yellow}!noclip{ChatColors.Default}.");
                break;
        }
    }

    [ConsoleCommand("css_dmg", "Affiche en continu les dégâts de bombe à votre position")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDmgCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        if (!_hudSlots.Remove(player.Slot))
        {
            _hudSlots.Add(player.Slot);
            Reply(player, $"{Prefix} HUD dégâts {ChatColors.Green}activé{ChatColors.Default} — déplace-toi pour voir les valeurs.");
        }
        else
        {
            _hudCache.Remove(player.Slot);
            Reply(player, $"{Prefix} HUD dégâts {ChatColors.Red}désactivé{ChatColors.Default}.");
        }
    }

    /// <summary>Config practice complète en une commande : cheats, rounds sans fin,
    /// bots passifs qui respawnent, argent illimité, et le C4 dans la main du joueur.</summary>
    private void CmdPrac(CCSPlayerController? player)
    {
        var settings = new[]
        {
            "sv_cheats 1",
            "mp_ignore_round_win_conditions 1",
            "mp_warmup_end",
            "mp_freezetime 0",
            "mp_roundtime 60",
            "mp_roundtime_defuse 60",
            "mp_maxmoney 60000",
            "mp_startmoney 60000",
            "mp_buytime 9999",
            "mp_buy_anywhere 1",
            "mp_autoteambalance 0",
            "mp_limitteams 0",
            "mp_respawn_on_death_ct 1",
            "mp_respawn_on_death_t 1",
            "sv_infinite_ammo 1",
            "bot_kick",
            "bot_join_after_player 0",
            "bot_stop 1",
            "mp_restartgame 1",
        };

        foreach (var cmd in settings)
            Server.ExecuteCommand(cmd);

        // Les bots arrivent après le restart, puis restent immobiles (bot_stop).
        AddTimer(2.0f, () =>
        {
            for (var i = 0; i < Config.PracBotCount; i++)
                Server.ExecuteCommand("bot_add ct");

            Server.ExecuteCommand("bot_stop 1");
        });

        if (player != null && player.IsValid)
            AddTimer(3.5f, () => GiveC4(player));

        Reply(player, $"{Prefix} Mode practice activé : {Config.PracBotCount} bots CT passifs (respawn auto), rounds sans fin.");
        Reply(player, $"{Prefix} Pose la bombe puis {ChatColors.Yellow}css_bombsim record{ChatColors.Default} → {ChatColors.Yellow}spread{ChatColors.Default} → {ChatColors.Yellow}boom{ChatColors.Default}. Re-bombe : {ChatColors.Yellow}css_bombsim c4{ChatColors.Default}, vol : {ChatColors.Yellow}css_noclip{ChatColors.Default}.");
    }

    private void CmdAuto(CCSPlayerController? player)
    {
        _autoMode = !_autoMode;

        if (!_autoMode)
        {
            if (_campaignRunning) FinishCampaign("arrêt manuel");
            Reply(player, $"{Prefix} Mode auto {ChatColors.Red}désactivé{ChatColors.Default}.");
            return;
        }

        Reply(player, $"{Prefix} Mode auto {ChatColors.Green}activé{ChatColors.Default} : pose la bombe et laisse faire (mesure automatique du plus proche au plus loin, {Config.AutoMaxWaves} vagues max).");

        // Bombe déjà posée : on démarre immédiatement.
        if (!_campaignRunning && _plantPos != null && _heatmap != null
            && Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").Any(c => c.IsValid))
        {
            StartCampaign();
        }
    }

    private void CmdGiveC4(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive)
        {
            Reply(player, $"{Prefix} Commande joueur uniquement (vivant).");
            return;
        }

        GiveC4(player);
        Reply(player, $"{Prefix} C4 donné.");
    }

    private static void GiveC4(CCSPlayerController player)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;

        if (player.Team != CsTeam.Terrorist)
            player.SwitchTeam(CsTeam.Terrorist);

        player.GiveNamedItem("weapon_c4");
    }

    [ConsoleCommand("css_noclip", "Active/désactive le noclip (sv_cheats requis)")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNoclipCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return;

        player.ExecuteClientCommandFromServer("noclip");
    }

    private void CmdShow(CCSPlayerController? player, bool armored)
    {
        if (_heatmap == null)
        {
            Reply(player, $"{Prefix} Aucune donnée : pose la bombe d'abord, puis {ChatColors.Yellow}!bombsim record{ChatColors.Default}.");
            return;
        }

        _renderer.Render(_heatmap, Config, armored);
        if (_renderer.MarkerCount == 0)
        {
            Reply(player, $"{Prefix} Aucune mesure {(armored ? "avec" : "sans")} armure pour ce spot — lance {ChatColors.Yellow}!bombsim record{ChatColors.Default}.");
            return;
        }

        Reply(player, $"{Prefix} Heatmap affichée ({_renderer.MarkerCount} points, {(armored ? "avec" : "sans")} armure).");
        CmdLegend(player);
    }

    private void CmdRecord(CCSPlayerController? player)
    {
        if (_plantPos == null || _heatmap == null)
        {
            Reply(player, $"{Prefix} Pose d'abord la bombe, puis relance {ChatColors.Yellow}!bombsim record{ChatColors.Default}.");
            return;
        }

        _recorder?.Arm();
        Reply(player, $"{Prefix} Enregistrement armé : place des bots ({ChatColors.Yellow}!bombsim spread{ChatColors.Default}), " +
                      $"puis laisse exploser ou {ChatColors.Yellow}!bombsim boom{ChatColors.Default}.");
    }

    private void CmdSpread(CCSPlayerController? player)
    {
        if (_plantPos == null || _heatmap == null)
        {
            Reply(player, $"{Prefix} Pose d'abord la bombe.");
            return;
        }

        var moved = SpreadBots(_plantPos, _heatmap);
        Reply(player, moved > 0
            ? $"{Prefix} {moved} bots répartis sur les zones non mesurées. Ajoute des bots ({ChatColors.Yellow}bot_add{ChatColors.Default}) pour couvrir plus vite."
            : $"{Prefix} Aucun bot vivant à répartir — ajoute-en avec {ChatColors.Yellow}bot_add{ChatColors.Default}.");
    }

    private void CmdBoom(CCSPlayerController? player)
    {
        var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
        if (c4 == null || !c4.IsValid)
        {
            Reply(player, $"{Prefix} Aucune bombe posée.");
            return;
        }

        c4.C4Blow = Server.CurrentTime + 1f;
        Utilities.SetStateChanged(c4, "CPlantedC4", "m_flC4Blow");
        Reply(player, $"{Prefix} Détonation dans 1 seconde…");
    }

    private void CmdStatus(CCSPlayerController? player)
    {
        if (_heatmap == null)
        {
            Reply(player, $"{Prefix} Aucun spot actif. Pose une bombe pour charger/créer les données du spot.");
            return;
        }

        var unarmored = _heatmap.Samples.Count(s => !s.Armored);
        var armored = _heatmap.Samples.Count - unarmored;
        Reply(player, $"{Prefix} Spot ({_heatmap.PlantX:0}, {_heatmap.PlantY:0}, {_heatmap.PlantZ:0}) sur {_heatmap.Map} : " +
                      $"{unarmored} mesures sans armure, {armored} avec armure. " +
                      $"Enregistrement : {(_recorder is { Armed: true } ? "armé" : "inactif")}. " +
                      $"Lecture prédictive : {(_reader is { Enabled: true } ? "active" : "désactivée (champ schema non renseigné)")}.");
    }

    private void CmdLegend(CCSPlayerController? player)
    {
        Reply(player, $"{Prefix} Légende : {ChatColors.Green}0-25{ChatColors.Default} | {ChatColors.Yellow}25-50{ChatColors.Default} | " +
                      $"{ChatColors.Orange}50-80{ChatColors.Default} | {ChatColors.Red}80-99{ChatColors.Default} | {ChatColors.Purple}100+ (mortel){ChatColors.Default} HP");
    }

    // ------------------------------------------------------------------- hud

    private void OnTick()
    {
        if (_hudSlots.Count == 0) return;

        var recompute = Server.TickCount % 8 == 0;

        foreach (var slot in _hudSlots.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player == null || !player.IsValid)
            {
                _hudSlots.Remove(slot);
                _hudCache.Remove(slot);
                continue;
            }

            if (!player.PawnIsAlive) continue;

            if (recompute || !_hudCache.ContainsKey(slot))
                _hudCache[slot] = BuildHudText(player);

            var text = _hudCache[slot];
            if (text.Length > 0)
                player.PrintToCenterHtml(text);
        }
    }

    private string BuildHudText(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;
        if (pawn == null || origin == null) return "";

        // Priorité à la valeur prédite par le jeu si le champ schema est connu,
        // sinon interpolation depuis les mesures empiriques du spot.
        var damage = _reader?.TryRead(pawn) ?? _heatmap?.QueryDamage(origin, Config.GridSpacing * 1.75f);

        if (damage == null)
            return _heatmap == null ? "" : "<font color='#888888'>💣 pas de mesure ici</font>";

        var color = DamagePalette.ToHex(DamagePalette.ForDamage(damage.Value));
        var lethal = damage.Value >= 100f ? " — MORTEL" : "";
        return $"<font color='{color}'>💣 {damage.Value:0} HP{lethal}</font>";
    }

    // ------------------------------------------------------------------ bots

    /// <summary>Téléporte les bots vivants sur les cellules de grille encore non
    /// mesurées, en spirale autour du plant (même étage que le plant).</summary>
    private int SpreadBots(Vector plantPos, HeatmapData heatmap)
    {
        var bots = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.IsBot && p.PawnIsAlive)
            .Select(p => p.PlayerPawn.Value)
            .Where(pawn => pawn != null)
            .Cast<CCSPlayerPawn>()
            .ToList();

        if (bots.Count == 0) return 0;

        Server.ExecuteCommand("bot_stop 1");

        var spacing = Config.GridSpacing;
        var maxRing = (int)(Config.SpreadRadius / spacing);
        var moved = 0;

        for (var ring = 1; ring <= maxRing && moved < bots.Count; ring++)
        {
            for (var i = -ring; i <= ring && moved < bots.Count; i++)
            {
                for (var j = -ring; j <= ring && moved < bots.Count; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring) continue;

                    var x = plantPos.X + i * spacing;
                    var y = plantPos.Y + j * spacing;
                    if (heatmap.HasSampleNear(x, y, plantPos.Z, spacing * 0.6f)) continue;

                    bots[moved].Teleport(new Vector(x, y, plantPos.Z + 16f), new QAngle(), new Vector());
                    moved++;
                }
            }
        }

        return moved;
    }

    // ---------------------------------------------------------------- misc

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
            player.PrintToChat(message);
        else
            Server.PrintToConsole(message);
    }
}
