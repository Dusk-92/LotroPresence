# LOTRO Presence

Discord Rich Presence pour **The Lord of the Rings Online**, conçu pour rester aussi passif que possible côté jeu.

## Architecture

```text
LOTRO
  -> plugin Lua (API Turbine officielle)
  -> LotroPresence.plugindata
  -> bridge Windows .NET
  -> Discord Rich Presence
```

Le bridge **ne lit pas la mémoire de LOTRO**, n'injecte aucune DLL, n'intercepte pas le réseau et n'automatise aucune action en jeu.

## État du projet

V0.1 expérimentale :

- nom du personnage
- niveau
- classe (quand l'API l'identifie)
- état combat / hors combat
- taille du groupe
- forme d'ours du Béornide quand disponible
- détection automatique du fichier `.plugindata` le plus récent
- arrêt automatique du Rich Presence si le heartbeat du plugin devient trop ancien

## Installation du plugin LOTRO

Copier le contenu de `plugin/` dans :

```text
Documents\The Lord of the Rings Online\Plugins\
```

Puis dans LOTRO :

```text
/plugins refresh
/plugins load LotroPresence
```

## Bridge Discord

Le bridge se trouve dans `bridge/LotroPresence.Bridge`.

L'application Discord officielle du projet est déjà configurée avec l'Application ID :

```text
1550150231092502613
```

Il suffit donc de lancer le bridge pendant que **Discord Desktop** et **LOTRO** sont ouverts.

Le fichier `config.json` permet ensuite de modifier les options locales si besoin (image Discord, fréquence de lecture, délai du heartbeat, etc.). L'Application ID Discord est public et n'est pas un secret.

## Développement

Le bridge cible .NET 8 et utilise le paquet NuGet `DiscordRichPresence`.

```powershell
dotnet restore bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj
dotnet run --project bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj
```

## Limites connues

LOTRO n'expose pas directement toutes les informations que WoW expose à ses addons. En particulier, la zone courante demandera une méthode séparée et ne fait volontairement pas partie de cette première version.
