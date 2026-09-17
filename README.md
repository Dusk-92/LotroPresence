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

Le bridge ne lit pas la mémoire de LOTRO, n'injecte aucune DLL, n'intercepte pas le réseau et n'automatise aucune action en jeu.

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

Lancer ensuite `LotroPresence.exe` avec Discord Desktop ouvert.

L'application Discord du projet utilise par défaut l'Application ID public :

```text
1550150231092502613
```

## Configuration

`config.default.json` contient les valeurs distribuées avec l'application.

Pour personnaliser localement la configuration, créer un `config.json` à côté de `LotroPresence.exe`. Ce fichier local est prioritaire et est ignoré par Git.

Paramètres principaux :

- `HeartbeatTimeoutSeconds` : délai avant d'effacer une présence devenue inactive
- `PollIntervalMilliseconds` : fréquence de lecture du fichier courant
- `DiscoveryIntervalSeconds` : fréquence maximale d'un scan complet de `PluginData`
- `LargeImageKey` / `LargeImageText` : asset Discord optionnel

## Fiabilité et confidentialité

Le format `PluginData` est versionné (`schemaVersion = 4`). Le bridge refuse un schéma incompatible plutôt que d'afficher des données ambiguës.

Le serveur est déterminé uniquement lorsque le bridge retrouve explicitement le dossier du personnage dans `PluginData`, puis utilise son dossier parent. Il ne remonte jamais arbitrairement jusqu'au nom du compte LOTRO.

Le scan récursif complet de `PluginData` n'est plus effectué toutes les deux secondes : le bridge conserve le fichier actif et ne relance une découverte complète que lorsque cela est nécessaire.

Une seule instance du bridge peut fonctionner à la fois.

## Races récentes

Le plugin couvre notamment le Béornide, le Haut-Elfe, le Hache-forte et le Hobbit des Rivières. Pour ce dernier, une détection dynamique complète les noms d'énumération connus afin de rester compatible avec une documentation Lua parfois en retard sur le client.

## Développement

Le bridge cible .NET 8 et utilise le paquet NuGet `DiscordRichPresence`.

```powershell
dotnet restore bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --runtime win-x64
dotnet publish bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --configuration Release --runtime win-x64 --self-contained true
```

Le binaire accepte aussi :

```text
LotroPresence.exe --self-test
```

Le CI exécute ces auto-tests avant de publier une release.

## Releases

Les releases sont immuables : un tag existant n'est plus écrasé par un nouveau ZIP. Chaque release contient également un fichier `.sha256` permettant de vérifier l'intégrité du téléchargement.

Voir `NOTICE.md` pour la mention de projet non officiel.
