using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace BombSimulation.Damage;

/// <summary>
/// Lecture du champ "dégâts prédits" que le jeu calcule côté serveur depuis le
/// patch du 08/07/2026 (l'aperçu qui fait clignoter la barre de vie quand la
/// bombe est posée). Le nom exact du champ n'est pas encore identifié : il faut
/// dumper le schema d'un serveur à jour puis renseigner
/// PredictedDamageSchemaClass / PredictedDamageSchemaField dans la config.
/// Tant que le champ est vide, ce lecteur est inactif et le plugin s'appuie
/// uniquement sur les mesures empiriques.
/// </summary>
public class PredictedDamageReader
{
    private readonly string _schemaClass;
    private readonly string _schemaField;
    private bool _broken;

    public PredictedDamageReader(string schemaClass, string schemaField)
    {
        _schemaClass = schemaClass;
        _schemaField = schemaField;
    }

    public bool Enabled => !_broken && !string.IsNullOrWhiteSpace(_schemaField);

    public float? TryRead(CCSPlayerPawn pawn)
    {
        if (!Enabled) return null;

        try
        {
            return Schema.GetSchemaValue<float>(pawn.Handle, _schemaClass, _schemaField);
        }
        catch (Exception)
        {
            // Champ inexistant dans ce build : on se rabat définitivement sur l'empirique.
            _broken = true;
            return null;
        }
    }
}
