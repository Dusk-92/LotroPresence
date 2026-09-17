# LOTRO Presence

Discord Rich Presence minimal pour **The Lord of the Rings Online**.

## Affichage Discord

Béornide :

```text
Heimvald • Béornide niveau 28
Solo • Serveur Orcrist
```

Autre race/classe :

```text
Anarmir • Champion niveau 67
Homme • Communauté de 4 • Serveur Orcrist
```

LotroPresence affiche automatiquement :

- nom du personnage
- classe
- niveau
- race
- Solo ou Communauté de X
- serveur
- durée de session Discord

La zone n'est volontairement pas utilisée : l'API Lua LOTRO ne fournit pas une zone courante fiable et automatique.

### Règle spéciale Béornide

Le Béornide étant déjà affiché comme classe, sa race n'est pas répétée sur la deuxième ligne.

```text
Heimvald • Béornide niveau 28
Solo • Serveur Orcrist
```

## Architecture

```text
LOTRO
  -> plugin Lua (API Turbine)
  -> LotroPresence.plugindata
  -> bridge Windows .NET
  -> Discord Rich Presence
```

Le bridge ne lit pas la mémoire de LOTRO, n'injecte aucune DLL, n'intercepte pas le réseau et n'automatise aucune action en jeu. Il vérifie uniquement l'existence du processus LOTRO afin de savoir quand se fermer automatiquement.

## Installation

Copier le contenu de `plugin/` dans :

```text
Documents\The Lord of the Rings Online\Plugins\
```

Puis dans LOTRO :

```text
/plugins refresh
/plugins load LotroPresence
```

Lancer ensuite `LotroPresence.exe`. Le bridge fonctionne en arrière-plan sans fenêtre de console et peut être démarré avant LOTRO ou avant Discord.

L'application Discord du projet utilise par défaut l'Application ID public :

```text
1550150231092502613
```

### Lancement automatique avec Steam

Pour démarrer LotroPresence automatiquement quand LOTRO est lancé depuis Steam, ajouter dans **Steam > Bibliothèque > The Lord of the Rings Online > Propriétés > Options de lancement** :

```text
cmd /c "start \"\" \"C:\CHEMIN\VERS\LotroPresence.exe\" & %command%"
```

Adapter uniquement le chemin vers `LotroPresence.exe`.

`LotroPresence.exe` est compilé comme application Windows : aucune fenêtre de console permanente n'est affichée. Il reste visible dans le Gestionnaire des tâches.

Le bridge attend d'avoir détecté le client LOTRO (`lotroclient64.exe` ou `lotroclient.exe`). Une fois LOTRO détecté :

- rester sur l'écran de sélection des personnages ne ferme pas LotroPresence ;
- changer de personnage ne ferme pas LotroPresence ;
- fermer réellement LOTRO fait disparaître le processus du jeu ;
- si le processus reste absent pendant environ 10 secondes, LotroPresence se ferme automatiquement.

## Configuration

`config.default.json` contient les valeurs distribuées avec l'application.

Pour personnaliser localement la configuration, créer un `config.json` à côté de `LotroPresence.exe`. Ce fichier local est prioritaire et est ignoré par Git.

Paramètres principaux :

- `HeartbeatTimeoutSeconds` : délai avant d'effacer une présence devenue inactive
- `PollIntervalMilliseconds` : fréquence de lecture du fichier courant
- `DiscoveryIntervalSeconds` : fréquence du contrôle des personnages actifs
- `LargeImageKey` / `LargeImageText` : asset Discord optionnel

## Fiabilité et confidentialité

Le format `PluginData` est versionné (`schemaVersion = 4`). Le bridge refuse un schéma incompatible plutôt que d'afficher des données ambiguës.

Le serveur n'est accepté que si `LotroPresence.plugindata` se trouve directement dans un dossier portant exactement le nom du personnage ; le dossier parent immédiat est alors utilisé comme serveur. Si cette structure stricte n'est pas reconnue, aucun serveur n'est affiché. Le bridge ne remonte jamais arbitrairement l'arborescence jusqu'au nom du compte LOTRO.

Les écritures `PluginData` côté Lua utilisent le callback de `Turbine.PluginData.Save`. Le heartbeat et l'empreinte ne sont validés qu'après confirmation de la sauvegarde ; une erreur déclenche une nouvelle tentative automatique sans casser la boucle du plugin.

Lors du déchargement du plugin, une écriture finale `active = false` est toujours demandée, même si une sauvegarde précédente attend encore son callback.

Si LOTRO retourne une classe ou une race inconnue, le plugin affiche une seule alerte avec l'ID concerné puis continue avec les informations disponibles.

Le bridge conserve le fichier courant pour la lecture rapide, mais contrôle périodiquement les autres `LotroPresence.plugindata`. Cela permet de basculer vers un nouveau personnage actif sans attendre l'expiration de l'ancien fichier. Un fichier `active = false`, même plus récent, n'est jamais choisi comme présence.

Une seule instance du bridge peut fonctionner à la fois.

## Races et classes récentes

Le plugin couvre notamment le Béornide, le Haut-Elfe, le Hache-forte et le Hobbit des Rivières. Pour ce dernier, une détection dynamique complète les noms d'énumération connus afin de rester compatible avec une documentation Lua parfois en retard sur le client.

Le Marin accepte les noms d'énumération `Mariner` et `Corsair` pour rester compatible avec les différentes versions de l'API LOTRO observées.

## Développement

Le bridge cible .NET 8 et utilise le paquet NuGet `DiscordRichPresence`.

Le SDK de build est fixé dans `global.json` et les dépendances NuGet transitives sont verrouillées dans `packages.lock.json`.

```powershell
dotnet restore bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --runtime win-x64 --locked-mode
dotnet publish bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore
```

Le binaire accepte aussi :

```text
LotroPresence.exe --self-test
```

Le CI vérifie :

- la syntaxe de `Main.lua` avec Lua 5.1
- la cohérence entre la version de `Main.lua` et le manifeste `LotroPresence.plugin`
- le restore NuGet en mode verrouillé
- la compilation Windows x64
- les self-tests du bridge
- la présence des fichiers requis dans le ZIP et son checksum SHA-256

Les GitHub Actions utilisées sont épinglées à des SHA de versions compatibles avec Node 24.

## Releases

Les pushes ordinaires sur `main` et les pull requests construisent, testent et valident le package avec des permissions en lecture seule, sans publier de release.

Une release est créée uniquement lorsqu'un tag `v*` est poussé. Le job de release effectue dans **le même job** le restore verrouillé, la compilation, les self-tests, la création du ZIP, la validation de son contenu et de son SHA-256, puis publie exactement ce ZIP. Il ne dépend donc pas du stockage GitHub Actions Artifacts.

Le tag doit correspondre à la version déclarée dans `LotroPresence.plugin` (par exemple `v0.4.3-alpha` pour la version `0.4.3`). Seul ce job de release reçoit `contents: write`.

Les releases restent immuables : une release existante n'est jamais écrasée par un nouveau ZIP. Chaque release contient un fichier `.sha256` permettant de vérifier l'intégrité du téléchargement. Le ZIP contient aussi `README.md` et `NOTICE.md`.

Voir `NOTICE.md` pour la mention de projet non officiel.
