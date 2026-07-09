# CS2 Bomb Simulation

Plugin serveur [CounterStrikeSharp](https://docs.cssharp.dev/) pour **simuler l'explosion de la bombe** et visualiser les dégâts directement au sol, sous forme de heatmap colorée. Pensé pour les serveurs d'entraînement : diagnostiquer les positions safe / dangereuses selon le spot de plant.

## Contexte : le patch du 08/07/2026

Depuis la mise à jour Season 5, la bombe n'inflige plus des dégâts instantanés à la détonation :

- une **onde de choc** se propage depuis le point de plant et applique les dégâts quand elle atteint chaque joueur ;
- l'onde est **bloquée par les murs** et se **dissipe dans les angles** ;
- les dégâts suivent des **valeurs de simulation précalculées, intégrées dans la map compilée**.

Conséquence : impossible de calculer les dégâts avec une formule de distance (l'ancienne gaussienne de CS:GO). Ce plugin les **mesure depuis le jeu lui-même**. Comme les valeurs sont précalculées par map, une mesure faite pour un spot de plant reste valable tant que Valve ne recompile pas la map — d'où le cache par `(map, position de plant)`.

## Fonctionnement

1. **Mesure empirique** — on pose la bombe, on répartit des bots sur une grille autour du plant, on laisse exploser. Chaque `player_hurt` infligé par la `planted_c4` devient une mesure : position de la victime au moment où l'onde l'atteint + dégâts bruts (les bots reçoivent des milliers de HP pour que la valeur ne soit pas plafonnée). On replante au même endroit et on répète pour densifier.
2. **Affichage au sol** — un vrai décal peint sur les textures n'est pas possible depuis un plugin serveur ; on affiche une grille de croix colorées (entités `beam`) plaquées au sol : vert → jaune → orange → rouge → **violet = mortel (100+)**.
3. **HUD live** (`!dmg`) — affiche en continu, au centre de l'écran, les dégâts estimés à ta position (interpolation des mesures voisines).
4. **Lecture prédictive** *(préparé, à activer)* — le jeu calcule désormais lui-même un aperçu des dégâts par joueur (la barre de vie clignote quand la bombe est posée). Si ce champ est exposé côté serveur, le renseigner dans la config (`PredictedDamageSchemaField`) permet de lire la valeur exacte en continu, et d'échantillonner une zone en téléportant un bot sonde — sans faire exploser quoi que ce soit. Le nom du champ reste à identifier en dumpant le schema d'un serveur à jour.

## Commandes

| Commande | Effet |
|---|---|
| `!bombsim prac` | Config practice complète : sv_cheats, rounds sans fin, bots CT passifs avec respawn, argent illimité, C4 en main |
| `!bombsim auto` | **Mode auto** : pose la bombe et le plugin fait tout — vagues d'explosions enchaînées (spread → boom → respawn → replant automatique de la bombe au même endroit) jusqu'à couvrir toute la zone, puis affiche la heatmap |
| `!bombsim stop` | Arrête la campagne auto en cours |
| `!bombsim c4` | Redonne un C4 (et passe côté T si besoin) |
| `!noclip` | Active/désactive le vol libre |
| `!bombsim record` | Arme l'enregistrement (bombe posée requise) : boost les HP des bots, capture les dégâts à l'explosion, sauvegarde et affiche la heatmap |
| `!bombsim spread` | Téléporte les bots vivants sur les cases non encore mesurées autour du plant |
| `!bombsim boom` | Fait détoner la bombe posée dans 1 seconde (pas besoin d'attendre les 40 s) |
| `!bombsim show [armored]` | Affiche la heatmap du spot courant (mesures sans / avec armure) |
| `!bombsim clear` | Efface l'affichage |
| `!bombsim status` | Nombre de mesures pour le spot, état de l'enregistreur |
| `!bombsim legend` | Légende des couleurs |
| `!dmg` | Active/désactive le HUD des dégâts à ta position |

## Workflow type (serveur practice)

```
!bombsim prac     // config practice + bots passifs + C4 en main, tout-en-un
!bombsim auto     // active le mode automatique
// pose la bombe sur le spot à étudier… et c'est tout :
// le plugin enchaîne les vagues (spread → boom → respawn → replant auto)
// du plus proche au plus loin, puis affiche la heatmap complète
!dmg              // balade-toi pour lire les valeurs exactes
```

Le mode manuel (`record` / `spread` / `boom` / `c4`) reste disponible pour des mesures ciblées.

> 💡 Tant que la signature `Host_Say` du gamedata n'est pas corrigée (casse du patch
> du 08/07/2026), les commandes chat `!`/`.` sont muettes : utiliser les équivalents
> console (`css_bombsim prac`, `css_dmg`, `css_noclip`, …).

## Récupérer le plugin

### Option A — GitHub Actions (aucun outil requis)

Chaque push déclenche le workflow **Build** : dans l'onglet *Actions* du dépôt, ouvrir le dernier run et télécharger l'artifact `BombSimulation` (le dossier prêt à déposer sur le serveur). Un tag `v*` (ex. `v0.1.0`) publie en plus une release avec le zip attaché.

### Option B — build local

1. Installer le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) :
   - **Windows** : `winget install Microsoft.DotNet.SDK.10`
   - **Linux (Ubuntu/Debian)** : `sudo apt-get install -y dotnet-sdk-10.0`
2. À la racine du dépôt :

   ```bash
   dotnet publish src/BombSimulation -c Release -o out/BombSimulation
   ```

Le dossier `out/BombSimulation/` contient uniquement le plugin (la référence CounterStrikeSharp est exclue du build : le serveur fournit déjà ces DLL — ne jamais les copier avec le plugin).

## Installation sur le serveur

Prérequis : [Metamod:Source](https://www.sourcemm.net/) + [CounterStrikeSharp](https://docs.cssharp.dev/docs/guides/getting-started.html).

Copier le dossier obtenu (artifact ou `out/BombSimulation/`) dans :

```
game/csgo/addons/counterstrikesharp/plugins/BombSimulation/
```

La config est générée au premier lancement dans `addons/counterstrikesharp/configs/plugins/BombSimulation/BombSimulation.json` (espacement de grille, HP des bots, mesures avec armure, champ schema prédictif…). Les heatmaps sont sauvegardées en JSON dans le dossier `data/` du plugin.

## Roadmap

- [x] Phase 1 — mesure empirique via bots + événements `player_hurt`
- [x] Phase 2 — heatmap au sol (beams), HUD live `!dmg`, détonation rapide, répartition des bots
- [x] Phase 3 — cache JSON par `(map, spot de plant)`
- [x] Mode auto : campagne de vagues enchaînées avec replant automatique de la bombe
- [x] ~~Identifier le champ schema de l'aperçu de dégâts~~ → **conclu : il n'existe pas côté serveur.** L'aperçu (barre de vie qui clignote) est calculé côté client à partir des données précalculées de la map ; aucun champ réseau, aucun user message dédié (vérifié par diff du schema complet avant/après patch, build 14168)
- [ ] Appeler la fonction serveur de lookup des dégâts précalculés via signature mémoire (dès que la communauté publie les patterns du nouveau libserver.so) → heatmap instantanée sans explosion
- [ ] Parser les données de dégâts baked directement depuis la map compilée (~171k valeurs/map)
- [ ] Comparaison côte à côte de deux spots de plant

> ⚠️ Ce plugin n'a pas encore été validé en jeu contre le nouveau système d'onde de choc : les noms d'événements/propriétés utilisés sont ceux connus de CounterStrikeSharp 1.0.370. Toute divergence constatée sur un serveur à jour est à remonter en issue.
