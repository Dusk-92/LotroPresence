# LOTRO Presence

Discord Rich Presence minimal pour **The Lord of the Rings Online**.

## Affichage Discord

```text
Anarmir • Champion niveau 67
Bree • Orcrist
```

LotroPresence n'affiche volontairement que :

- nom du personnage
- classe
- niveau
- zone / lieu courant
- serveur
- durée de session Discord

Pas d'état combat, de groupe, de cible ou de forme de classe.

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

## Zone courante

L'API Lua LOTRO ne permet pas à un plugin de demander automatiquement la position ou la zone du joueur. LotroPresence utilise donc l'alias natif `;loc` quand le joueur le lui transmet.

Pour actualiser la zone :

```text
/lp ;loc
```

La zone obtenue est mémorisée pour ce personnage et reste affichée jusqu'à la prochaine actualisation.

Commandes utiles :

```text
/lp ;loc
/lp zone Nom de zone
/lp clear
```

`/lp zone ...` permet de corriger manuellement le nom affiché si `;loc` renvoie un sous-lieu plutôt que la zone souhaitée.

## Serveur

Le serveur est déterminé uniquement lorsque le bridge retrouve explicitement le dossier du personnage dans `PluginData`. Il prend alors le dossier parent comme serveur. Si cette structure n'est pas reconnue, aucun serveur n'est affiché afin de ne jamais confondre le serveur avec le nom du compte LOTRO.

## Développement

Le bridge cible .NET 8 et utilise le paquet NuGet `DiscordRichPresence`.

```powershell
dotnet restore bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj --runtime win-x64
dotnet run --project bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj
```
