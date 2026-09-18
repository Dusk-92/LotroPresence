# LOTRO Presence

Discord Rich Presence minimal pour **The Lord of the Rings Online**.

## Affichage Discord

Béornide :

```text
Heimvald • Béornide • Niveau 28
Hauts du Nord • Solo • Serveur Orcrist
```

Autre race/classe :

```text
Altherian • Homme • Champion • Niveau 28
Pays de Bree • Solo • Serveur Orcrist
```

LotroPresence affiche automatiquement :

- nom du personnage
- classe
- niveau
- race
- région courante lorsqu'elle est détectée
- Solo ou Communauté de X
- serveur
- durée de session LOTRO continue, sans remise à zéro lors d'un changement de personnage
- icône Discord de la classe active ou de la sélection de personnage

La région est détectée passivement à partir des messages système de changement de canaux régionaux de LOTRO. Le parseur accepte le format natif `Entered the <région> - Regional channel.` ainsi que plusieurs variantes localisées FR/DE. Aucun `/loc`, `/who`, clic ou automatisation d'action en jeu n'est nécessaire.

La valeur correspond à la région de chat (par exemple **Hauts du Nord**), pas forcément à une petite sous-zone comme Nan Wathren. Lorsqu'une région est reconnue, le plugin affiche `LotroPresence : région détectée : ...` dans le chat. Si un message régional n'est pas compris, la version alpha l'affiche une seule fois pour faciliter le diagnostic. La dernière région connue du personnage est conservée entre les chargements.

### Règle spéciale Béornide

Le Béornide étant déjà affiché comme classe, sa race n'est pas répétée sur la première ligne.

```text
Heimvald • Béornide • Niveau 28
Hauts du Nord • Solo • Serveur Orcrist
```

## Architecture

```text
LOTRO
  -> plugin Lua (API Turbine)
  -> LotroPresence.plugindata
  -> bridge Windows .NET
  -> Discord Rich Presence
```

Le bridge ne lit pas la mémoire de LOTRO, n'injecte aucune DLL, n'intercepte pas le réseau et n'automatise aucune action en jeu. Il vérifie uniquement l'existence du processus LOTRO dans la session Windows courante afin de gérer la durée de session et sa fermeture automatique.

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
"C:\CHEMIN\VERS\LotroPresence.exe" --steam-launch %command%
```

Si des options LOTRO sont déjà présentes, les conserver après `%command%`. Exemple :

```text
"F:\LotroPresence\LotroPresence.exe" --steam-launch %command% -skiprawdownload
```

Le mode `--steam-launch` relance directement la commande LOTRO fournie par Steam, sans passer par `cmd.exe`. Les arguments sont transmis séparément, ce qui évite que des caractères spéciaux comme `>`, `&` ou `^` soient interprétés par un shell Windows.

Éviter de placer un nom d'utilisateur ou un mot de passe dans les options de lancement Steam : ces valeurs peuvent être visibles en clair dans Steam et dans la ligne de commande des processus concernés. LotroPresence n'a besoin d'aucun identifiant LOTRO pour fonctionner et ne journalise jamais les arguments de lancement Steam.

`LotroPresence.exe` est compilé comme application Windows : aucune fenêtre de console permanente n'est affichée. Il reste visible dans le Gestionnaire des tâches.

## Cycle de vie LOTRO

Le bridge surveille uniquement `lotroclient64.exe` et `lotroclient.exe` dans **la même session Windows que LotroPresence**.

Une fois le client détecté :

- le timer Discord commence une seule fois et reste continu entre sélection et personnages ;
- l'écran sans snapshot actif récent affiche « Sélection de personnage » avec l'asset `character_select` ;
- changer de personnage conserve le même timer de session ;
- si le processus LOTRO disparaît, la présence Discord est masquée immédiatement ;
- une période de grâce d'environ 10 secondes évite de fermer le bridge sur une disparition très brève du processus ;
- si LOTRO revient pendant cette grâce, la présence revient avec le même timer ;
- si LOTRO reste absent environ 10 secondes, le bridge se ferme proprement.

### Limite de l'état « Sélection de personnage »

LOTRO ne donne pas au bridge un signal externe fiable indiquant « écran de sélection ». Cet état est donc déduit de :

```text
client LOTRO présent + aucun snapshot PluginData actif récent
```

Par conséquent, si le plugin n'est pas chargé, échoue à écrire son PluginData ou devient temporairement indisponible alors qu'un personnage est en jeu, Discord peut afficher « Sélection de personnage ». Corriger totalement cette ambiguïté demanderait une méthode plus intrusive que le projet choisit volontairement de ne pas utiliser.

## Configuration

`config.default.json` contient les valeurs distribuées avec l'application.

Pour personnaliser localement la configuration, créer un `config.json` à côté de `LotroPresence.exe`. Ce fichier local est ignoré par Git et seules les clés qu'il contient remplacent les valeurs correspondantes de `config.default.json`.

Paramètres principaux :

- `HeartbeatTimeoutSeconds` : 40 à 300 secondes
- `PollIntervalMilliseconds` : 500 à 30000 ms
- `DiscoveryIntervalSeconds` : 5 à 120 secondes
- `LargeImageKey` : asset Discord optionnel, 256 caractères maximum
- `LargeImageText` : texte optionnel, 128 caractères maximum

Les types et bornes sont validés au démarrage. Une clé inconnue est ignorée mais signalée dans le journal. Une valeur connue avec un type ou une valeur invalide empêche le démarrage afin d'éviter un comportement ambigu.

`DiscordApplicationId` doit être un identifiant Discord numérique valide. La variable d'environnement `LOTROPRESENCE_DISCORD_APP_ID` peut toujours le remplacer.

L'icône principale de l'application Discord peut être utilisée automatiquement par Discord lorsque `LargeImageKey` reste vide. Les petites icônes de classes et `character_select` restent envoyées séparément par LotroPresence.

## Diagnostic

La version 0.4.8 ajoute un journal local rotatif, utile puisque le bridge n'a pas de fenêtre de console.

Emplacement :

```text
%LocalAppData%\LotroPresence\LotroPresence.log
```

Le fichier est limité à environ 512 Kio puis conservé avec trois rotations (`.1`, `.2`, `.3`). Le journal contient uniquement des événements techniques comme le démarrage, la détection de LOTRO, la connexion Discord, les erreurs de configuration ou d'IPC.

LotroPresence ne journalise pas les arguments Steam, les mots de passe, les identifiants LOTRO ni le chemin complet d'un fichier PluginData.

### Dépannage rapide

Si la présence n'apparaît pas :

1. vérifier que le plugin LOTRO est chargé ;
2. vérifier que Discord est lancé ;
3. attendre quelques secondes après la connexion du personnage ;
4. consulter `%LocalAppData%\LotroPresence\LotroPresence.log` ;
5. vérifier un éventuel `config.json` local invalide.

## Fiabilité et confidentialité

Le format `PluginData` est versionné (`schemaVersion = 4`). Le bridge refuse un schéma incompatible plutôt que d'afficher des données ambiguës.

Un fichier `LotroPresence.plugindata` supérieur à 64 Kio est refusé. Une petite tolérance de 10 secondes est appliquée aux horodatages légèrement dans le futur afin de supporter une correction mineure de l'horloge Windows sans accepter indéfiniment un fichier incohérent.

Le serveur n'est accepté que si `LotroPresence.plugindata` se trouve directement dans un dossier portant exactement le nom du personnage ; le dossier parent immédiat est alors utilisé comme serveur. Si cette structure stricte n'est pas reconnue, aucun serveur n'est affiché. Le bridge ne remonte jamais arbitrairement l'arborescence jusqu'au nom du compte LOTRO.

Les écritures `PluginData` côté Lua utilisent le callback de `Turbine.PluginData.Save`. Le heartbeat et l'empreinte ne sont validés qu'après confirmation de la sauvegarde ; une erreur déclenche une nouvelle tentative automatique sans casser la boucle du plugin.

Lors du déchargement du plugin, une écriture finale `active = false` est toujours demandée. Si une ancienne sauvegarde `active = true` termine ensuite et que son callback s'exécute encore, le plugin réaffirme `active = false` pour réduire la course entre écritures asynchrones.

Si LOTRO retourne une classe ou une race inconnue, le plugin affiche une seule alerte avec l'ID concerné puis continue avec les informations disponibles.

Le bridge conserve le fichier courant pour la lecture rapide, mais contrôle périodiquement les autres `LotroPresence.plugindata`. Cela permet de basculer vers un nouveau personnage actif sans attendre l'expiration de l'ancien fichier. Un fichier `active = false`, même plus récent, n'est jamais choisi comme présence.

Une seule instance du bridge peut fonctionner par session Windows. Si Steam relance LotroPresence alors qu'une instance existe déjà, la nouvelle instance peut tout de même lancer LOTRO puis s'arrête, tandis que l'instance déjà active continue de gérer Discord.

Plusieurs clients LOTRO simultanés ne sont pas associés individuellement à un bridge : la présence suit le snapshot actif le plus récent. Cette configuration reste donc une limitation connue.

### Données envoyées à Discord

LotroPresence transmet localement au client Discord uniquement les informations nécessaires à la Rich Presence : nom du personnage, race, classe, niveau, région détectée, statut Solo/Communauté, serveur, timer de session et clés d'assets Discord. Leur visibilité finale dépend des paramètres de confidentialité et d'activité du compte Discord.

LotroPresence n'exploite aucun serveur de télémétrie ou backend propre au projet.

## Races et classes récentes

Le plugin couvre notamment le Béornide, le Haut-Elfe, le Hache-forte et le Hobbit des Rivières. Pour ce dernier, une détection dynamique complète les noms d'énumération connus afin de rester compatible avec une documentation Lua parfois en retard sur le client.

Le Marin accepte les noms d'énumération `Mariner` et `Corsair` pour rester compatible avec les différentes versions de l'API LOTRO observées.

Les 12 classes actuellement prises en charge ont chacune une clé d'asset Discord dédiée et sont contrôlées par les tests de régression.

## Développement

Le bridge cible **.NET 10 LTS**. Le SDK de build est fixé dans `global.json` et les dépendances NuGet transitives sont verrouillées dans `packages.lock.json`.

La version de projet est déclarée dans `VERSION`. Le pipeline vérifie automatiquement qu'elle correspond à :

- `Main.lua`
- `LotroPresence.plugin`
- `Version`, `AssemblyVersion`, `FileVersion` et `InformationalVersion` du projet .NET
- le tag de release lorsqu'il existe
- les métadonnées de l'EXE généré

Le build complet local utilise le même script que GitHub Actions :

```powershell
./build/Build-LotroPresence.ps1
```

Ce script effectue : restore verrouillé, audit des dépendances NuGet vulnérables, tests de régression, compilation, publication Windows x64 autonome, self-tests de l'EXE, smoke-test sans console, création du ZIP et validation SHA-256.

Le binaire accepte aussi :

```text
LotroPresence.exe --self-test
LotroPresence.exe --steam-launch <commande LOTRO> [arguments LOTRO...]
```

### Tests de régression

Le projet `tests/LotroPresence.Tests` couvre notamment :

- format Béornide et format race/classe/niveau
- Solo et Communauté
- les 12 mappings d'icônes de classes
- état de sélection sans doublon du nom du jeu
- surcharge et validation de `config.json`
- parsing PluginData, dont la région
- déduction stricte du serveur
- rejet d'un PluginData surdimensionné
- tolérance d'horloge et expiration
- conservation exacte des arguments du wrapper Steam

Le CI valide aussi la syntaxe de `Main.lua` avec Lua 5.1.

## Dépendances

Dependabot surveille chaque semaine les dépendances NuGet directes ainsi que les actions GitHub. Le build exécute également un audit des vulnérabilités NuGet connues, y compris transitives.

Les GitHub Actions utilisées dans le workflow sont épinglées à des SHA précis.

## Releases

Les pushes ordinaires sur `main` et les pull requests exécutent le même script de build, de tests et de packaging avec des permissions GitHub en lecture seule.

Une release est créée uniquement lorsqu'un tag `v*` est poussé. Le job de release reconstruit et reteste lui-même exactement le tag avant publication ; il ne réutilise pas un binaire produit par un autre job.

Le tag doit correspondre à la version déclarée dans `VERSION` (par exemple `v0.4.10-alpha` pour la version `0.4.10`). Seul le job de release reçoit `contents: write`.

Le workflow ne remplace jamais les assets d'une release déjà existante. Cette politique évite l'écrasement accidentel, mais ne doit pas être confondue avec la fonctionnalité GitHub « immutable releases » : la release elle-même n'est pas déclarée immuable par GitHub.

Chaque release contient un fichier `.sha256` permettant de vérifier l'intégrité du téléchargement. Le ZIP contient aussi `VERSION`, `README.md` et `NOTICE.md`.

Voir `NOTICE.md` pour la mention de projet non officiel.
