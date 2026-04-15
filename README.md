## AutoCAD AI (starter)

## Résumé du projet

Objectif: créer un **copilote IA pour AutoCAD 2024–2025** qui peut:

- **lire** le DWG (contexte avancé: unités, calques, blocs, styles, sélection…),
- **proposer un plan d’action** (agent) et **exécuter après confirmation**,
- fournir une **palette de prompt** (dockable / flottante),
- gérer un **workspace par projet** (à côté du DWG) pour logs, cache, génération LISP/scripts,
- fonctionner en **mode hybride**:
  - **local** via **LM Studio**,
  - **cloud** via **Anthropic**,
  - politique de données **paramétrable par projet** (ce qui peut “sortir de la machine”).

## Workspace “par projet” (à côté du DWG)

- Dossier racine: `(<dossier du DWG>)\.autocad-ai\`
- Sous-dossiers: `generated\lisp\`, `generated\scripts\`, `logs\`, `cache\`
- Config projet: `(<dossier du DWG>)\.autocad-ai\project.json`

Fallback si le DWG n’est pas enregistré: `Documents\AutoCAD-AI\workspace\`

### Commandes incluses (MVP)

- `AIWORKSPACE` : crée/valide la structure `.autocad-ai` et affiche les chemins.
- `AIOPENWS` : ouvre le dossier `.autocad-ai` dans l’explorateur.
- `AICONFIG` : crée/affiche la config projet (`project.json`).
- `AICHAT` : ouvre la palette IA (dockable/flottante).

### Structure

- `src/AutocadAI.Plugin/` : plugin AutoCAD (.NET Framework) + gestion du workspace par projet.

## Plan de travail

### MVP (base solide)

- **Plugin .NET**: commandes, gestion du workspace, logs.
- **Config par projet** (`project.json`):
  - politique de données: `local-only` / `minimal-cloud` / `sanitized-cloud`
  - moteur préféré: `auto` / `local` (LM Studio) / `cloud` (Anthropic)
  - endpoints (local) + clés/paramètres (cloud) via secrets locaux (pas dans le repo).
- **Palette IA** (WPF dans `PaletteSet`):
  - dockable + flottante (même UI)
  - affichage du workspace actif et de la politique de données
  - panneau “Plan / Diff” + bouton “Exécuter”
- **Cas d’usage texte**:
  - correction / réécriture de `MText` / `DBText` sélectionnés
  - cloud possible en “minimal” (envoi du texte uniquement)

### V1 (actions DWG)

- **Tables**:
  - création de tableaux (style + données)
  - “table depuis sélection” (liste de blocs/attributs)
- **Cartouche & pages**:
  - insertion d’un bloc cartouche + remplissage d’attributs (titre/date/page)
  - génération en série (ex: 18 étages espacés de 5000 en X)
- **Géométrie paramétrique**:
  - exemple: “chaise vue du dessus” (vérif unités + calque + dimensions)

### V2 (agent avancé)

- **Outils (tools) structurés**: l’IA produit des actions JSON (pas du texte libre)
- **Prévisualisation / validation**: estimations d’impact (entités créées, calques, blocs)
- **Sandbox LISP**:
  - génération dans `generated\lisp\`
  - exécution contrôlée (opt-in) + journalisation

### Build (principe)

Les plugins AutoCAD référencent les DLL Autodesk installées sur la machine (AutoCAD 2024/2025).
Le fichier projet contient des références **à ajuster** vers tes DLL locales, typiquement:

- `AcCoreMgd.dll`
- `AcDbMgd.dll`
- `AcMgd.dll`

Une fois compilé, on charge le plugin via `NETLOAD` dans AutoCAD.

