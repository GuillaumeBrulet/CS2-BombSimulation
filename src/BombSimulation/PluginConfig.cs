using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace BombSimulation;

public class PluginConfig : BasePluginConfig
{
    /// <summary>Espacement de la grille d'échantillonnage / d'affichage, en unités.</summary>
    [JsonPropertyName("GridSpacing")]
    public float GridSpacing { get; set; } = 64f;

    /// <summary>Taille des marqueurs (croix) dessinés au sol, en unités.</summary>
    [JsonPropertyName("MarkerSize")]
    public float MarkerSize { get; set; } = 36f;

    [JsonPropertyName("BeamWidth")]
    public float BeamWidth { get; set; } = 2.5f;

    /// <summary>Nombre maximum de marqueurs affichés (2 beams par marqueur).</summary>
    [JsonPropertyName("MaxMarkers")]
    public int MaxMarkers { get; set; } = 350;

    /// <summary>HP donnés aux bots pendant l'enregistrement, pour que player_hurt
    /// reporte les dégâts bruts sans être plafonné par les 100 HP de base.</summary>
    [JsonPropertyName("RecorderBotHealth")]
    public int RecorderBotHealth { get; set; } = 2000;

    /// <summary>Donner kevlar+casque aux bots pendant l'enregistrement (mesure "avec armure").</summary>
    [JsonPropertyName("RecorderBotsArmored")]
    public bool RecorderBotsArmored { get; set; } = false;

    /// <summary>Durée maximale (s) de capture des player_hurt après bomb_exploded —
    /// l'onde de choc met du temps à atteindre les joueurs éloignés. La fenêtre se
    /// ferme plus tôt dès que l'onde a fini de frapper (voir RecordQuietSeconds).</summary>
    [JsonPropertyName("RecordWindowSeconds")]
    public float RecordWindowSeconds { get; set; } = 6f;

    /// <summary>Fermeture anticipée de la fenêtre de capture : si aucun player_hurt
    /// de la bombe n'arrive pendant ce délai (s), l'onde est considérée dissipée
    /// et la vague se termine sans attendre RecordWindowSeconds.</summary>
    [JsonPropertyName("RecordQuietSeconds")]
    public float RecordQuietSeconds { get; set; } = 1.0f;

    /// <summary>Rayon (unités) autour du plant dans lequel !bombsim spread répartit les bots.</summary>
    [JsonPropertyName("SpreadRadius")]
    public float SpreadRadius { get; set; } = 1000f;

    /// <summary>Arrondi de la position de plant pour la clé de cache (unités).</summary>
    [JsonPropertyName("PlantKeyRounding")]
    public float PlantKeyRounding { get; set; } = 32f;

    /// <summary>Nombre de bots ajoutés par !bombsim prac.</summary>
    [JsonPropertyName("PracBotCount")]
    public int PracBotCount { get; set; } = 12;

    /// <summary>Nombre de bots visé pendant une campagne auto (ajoutés au lancement
    /// si besoin). Plus il y a de bots, moins il faut de rounds pour couvrir la zone :
    /// 1 bot = 1 mesure par round.</summary>
    [JsonPropertyName("AutoBotCount")]
    public int AutoBotCount { get; set; } = 40;

    /// <summary>Garde-fou : nombre maximum de rounds (vagues) en mode auto. La
    /// campagne s'arrête d'elle-même bien avant, quand toutes les cases sont
    /// mesurées ou hors de portée de l'onde.</summary>
    [JsonPropertyName("AutoMaxWaves")]
    public int AutoMaxWaves { get; set; } = 40;

    /// <summary>Délai (s) entre le placement des bots et la détonation.</summary>
    [JsonPropertyName("AutoSpreadSettleSeconds")]
    public float AutoSpreadSettleSeconds { get; set; } = 0.5f;

    /// <summary>Délai (s) après le restart de round de la campagne avant de
    /// replanter la bombe et lancer la vague suivante (laisse le temps aux spawns).</summary>
    [JsonPropertyName("AutoRespawnDelaySeconds")]
    public float AutoRespawnDelaySeconds { get; set; } = 1.5f;

    /// <summary>Classe schema où lire les dégâts prédits par le jeu (aperçu natif du patch
    /// du 08/07/2026). Laisser le champ vide tant que le nom exact n'est pas identifié.</summary>
    [JsonPropertyName("PredictedDamageSchemaClass")]
    public string PredictedDamageSchemaClass { get; set; } = "CCSPlayerPawn";

    /// <summary>Nom du champ schema des dégâts prédits. Vide = désactivé (on utilise
    /// alors uniquement les mesures empiriques).</summary>
    [JsonPropertyName("PredictedDamageSchemaField")]
    public string PredictedDamageSchemaField { get; set; } = "";
}
