# 🎮 LOTRO Presence

Discord Rich Presence minimal pour **The Lord of the Rings Online**.

**🌍 Langue :** 🇫🇷 Français

---

## 🇫🇷 Français

### 📖 Présentation

**LOTRO Presence** affiche automatiquement sur Discord les principales informations du personnage LOTRO grâce à un petit plugin Lua et un bridge Windows.

Exemple :

```text
Heimvald • Béornide • Niveau 28
Solo • Serveur Orcrist
```

### ✨ Fonctionnalités

- Nom du personnage.
- Classe.
- Niveau.
- Race.
- Solo ou Communauté de X.
- Serveur.
- Durée de session LOTRO continue.
- Icône Discord de la classe active.
- État **Sélection de personnage** lorsqu’aucun personnage actif récent n’est détecté.
- Règle spéciale Béornide : la race n’est pas répétée lorsque la classe est déjà Béornide.
- Journal local rotatif pour le diagnostic.
- Bridge non intrusif : pas de lecture mémoire, pas d’injection DLL, pas d’interception réseau.

### 📦 Installation

Copie le contenu du dossier `plugin/` dans :

```text
Documents\The Lord of the Rings Online\Plugins\
```

Puis dans LOTRO :

```text
/plugins refresh
/plugins load LotroPresence
```

Lance ensuite `LotroPresence.exe` sous Windows. Discord doit être lancé pour afficher la présence.

Pour un lancement automatique via Steam :

```text
"C:\CHEMIN\VERS\LotroPresence.exe" --steam-launch %command%
```

### 🎮 Utilisation

Une fois le plugin chargé et le bridge lancé, la présence Discord se met à jour automatiquement. Le bridge peut être lancé avant LOTRO ou avant Discord.

### ⌨️ Commandes

Aucune commande en jeu n’est nécessaire après le chargement du plugin.

Le mode `--steam-launch` permet au bridge de relancer proprement la commande LOTRO transmise par Steam.

### ⚙️ Sauvegardes & réglages

- Le plugin écrit un petit snapshot `PluginData` par personnage.
- `config.default.json` contient les valeurs distribuées.
- Un `config.json` local peut remplacer uniquement les réglages souhaités.
- Le journal se trouve dans :

```text
%LocalAppData%\LotroPresence\LotroPresence.log
```

### 🌍 Langues

L’affichage et les libellés du projet sont actuellement maintenus en **français**.

### ⚠️ Limites / notes

La zone courante n’est volontairement pas affichée : l’API Lua LOTRO ne fournit pas une détection automatique suffisamment fiable pour ce projet.

L’état **Sélection de personnage** est déduit de la présence du client LOTRO et de l’absence de snapshot récent. Si le plugin n’est pas chargé ou ne peut pas écrire ses données, cet état peut donc apparaître alors qu’un personnage est connecté.

### 🐛 Bugs & suggestions

Utilise les [Issues GitHub](https://github.com/Dusk-92/LotroPresence/issues).

### 🙏 Crédits

Développement et maintenance : **Dusk-92**.
