# LOTRO Presence

Discord Rich Presence minimal pour **The Lord of the Rings Online**.

## Affichage Discord

Exemple avec un Béornide :

```text
Heimvald • Béornide niveau 28
Solo • Serveur Orcrist
```

Exemple avec une autre race/classe :

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

### Règle spéciale Béornide

Le Béornide étant déjà affiché comme classe, sa race n'est pas répétée sur la deuxième ligne.

Ainsi :

```text
Heimvald • Béornide niveau 28
Solo • Serveur Orcrist
```

et non :

```text
Heimvald • Béornide niveau 28
Béornide • Solo • Serveur Orcrist
```

## Architecture

```text
LOTRO
  -> plugin Lua (API Turbine officielle)
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

L'application Discord du projet utilise l'Application ID :

```text
1550150231092502613
```

## Serveur

Le serveur est déterminé uniquement lorsque le bridge retrouve explicitement le dossier du personnage dans `PluginData`. Il prend alors le dossier parent comme serveur. Si cette structure n'est pas reconnue, aucun serveur n'est affiché afin de ne jamais confondre le serveur avec le nom du compte LOTRO.

## Développement

Le bridge cible .NET 8 et utilise le paquet NuGet `DiscordRichPresence`.

```powershell
dotnet restore bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --runtime win-x64
dotnet run --project bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj
```
