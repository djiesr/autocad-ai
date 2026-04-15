## Primitives AutoCAD AI (JSON)

Le plugin exécute des actions décrites en JSON:

```json
{
  "actions": [
    { "type": "..." }
  ]
}
```

### Règles générales

- Les actions sont exécutées **dans l’espace courant** (Model/Layout actif).
- Les coordonnées sont en unités du dessin.
- Si une action a besoin d’un point (insert/center/from/to) et qu’il n’est pas fourni, l’IA doit demander ce point au user (au lieu d’inventer).

## Liste des primitives supportées

## Lecture / inspection

### `get_drawing_info`

Retourne des infos de base (fichier, unités, variables).

```json
{"actions":[{"type":"get_drawing_info"}]}
```

### `get_variable` / `set_variable`

```json
{"actions":[{"type":"get_variable","name":"LTSCALE"}]}
```

```json
{"actions":[{"type":"set_variable","name":"LTSCALE","value":50}]}
```

### `list_layers`

```json
{"actions":[{"type":"list_layers"}]}
```

### `list_blocks`

```json
{"actions":[{"type":"list_blocks"}]}
```

### `web_search`

Recherche web (DuckDuckGo) et retourne une liste de résultats.

```json
{"actions":[{"type":"web_search","query":"10 plus hauts sommets du Québec","max_results":5}]}
```

## Interactif (demande à l’utilisateur)

### `get_point`

Demande un point à l’utilisateur (pick dans AutoCAD) et stocke dans une variable.

```json
{"actions":[{"type":"get_point","var":"p1","message":"Point d’insertion"}]}
```

Réutilisation:
- `"$p1.x"`, `"$p1.y"`, `"$p1.z"`

### `get_text`

Demande un texte à l’utilisateur et stocke dans une variable.

```json
{"actions":[{"type":"get_text","var":"t1","message":"Entrez le titre"}]}
```

Réutilisation:
- `"$t1"`

### `set_var`

Crée une variable (valeur JSON quelconque).

```json
{"actions":[{"type":"set_var","var":"w","value":120}]}
```

### `get_entity_properties`

```json
{"actions":[{"type":"get_entity_properties","handle":"1A2B"}]}
```

### `get_bounding_box`

```json
{"actions":[{"type":"get_bounding_box","handle":"1A2B"}]}
```

ou

```json
{"actions":[{"type":"get_bounding_box","handles":["1A2B","1A2C"]}]}
```

### `get_drawing_extents`

```json
{"actions":[{"type":"get_drawing_extents"}]}
```

### `read_block_attributes` / `update_block_attributes`

```json
{"actions":[{"type":"read_block_attributes","handle":"1A2B"}]}
```

```json
{"actions":[{"type":"update_block_attributes","handle":"1A2B","attributes":{"TITRE":"Nouveau"}}]}
```

## Sélection

### `select_entities`

Sélectionne par filtre et retourne des handles.

```json
{"actions":[{"type":"select_entities","types":["mtext","circle"],"layer":"AI","colorIndex":1}]}
```

### `get_selection`

Retourne la sélection active (si l’utilisateur a déjà sélectionné des objets).

```json
{"actions":[{"type":"get_selection"}]}
```

## Modification d’existant (handles)

### `move_entity`

```json
{"actions":[{"type":"move_entity","handle":"1A2B","dx":100,"dy":0,"dz":0}]}
```

### `rotate_entity`

Angle en radians.

```json
{"actions":[{"type":"rotate_entity","handle":"1A2B","angle":1.57079632679,"base":{"x":0,"y":0,"z":0}}]}
```

### `scale_entity`

```json
{"actions":[{"type":"scale_entity","handle":"1A2B","factor":2.0,"base":{"x":0,"y":0,"z":0}}]}
```

### `delete_entity`

```json
{"actions":[{"type":"delete_entity","handle":"1A2B"}]}
```

### `change_entity_properties`

```json
{"actions":[{"type":"change_entity_properties","handle":"1A2B","layer":"AI","colorIndex":2,"linetype":"Continuous"}]}
```

### `ensure_layer`

Crée un calque si absent.

```json
{"actions":[{"type":"ensure_layer","name":"AI","colorIndex":2}]}
```

- **name**: string (requis)
- **colorIndex**: int ACI (optionnel)

### `set_current_layer`

```json
{"actions":[{"type":"set_current_layer","name":"AI"}]}
```

### `draw_line`

```json
{"actions":[{"type":"draw_line","from":{"x":0,"y":0},"to":{"x":100,"y":0}}]}
```

### `draw_polyline`

```json
{"actions":[{"type":"draw_polyline","points":[{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":10}],"closed":false}]}
```

### `draw_rectangle`

Rectangle par origine (coin bas-gauche), largeur et hauteur.

```json
{"actions":[{"type":"draw_rectangle","origin":{"x":0,"y":0},"width":16,"height":14}]}
```

### `draw_circle`

```json
{"actions":[{"type":"draw_circle","center":{"x":24,"y":-24},"diameter":100}]}
```

Paramètres:
- **center**: `{x,y,z?}` (requis)
- **radius**: number (optionnel)
- **diameter**: number (optionnel)

### `create_mtext`

Crée un MTEXT.

```json
{"actions":[{"type":"create_mtext","insert":{"x":0,"y":0},"text":"Définition: un carré...","height":2.5,"width":120,"layer":"AI"}]}
```

### `create_text`

Crée un TEXT simple.

```json
{"actions":[{"type":"create_text","insert":{"x":0,"y":0},"text":"Bonjour","height":2.5,"layer":"AI"}]}
```

### `update_single_mtext`

Met à jour le seul MTEXT trouvé dans l’espace courant.

```json
{"actions":[{"type":"update_single_mtext","mode":"replace","text":"Nouveau texte"}]}
```

- **mode**: `"replace"` (défaut) ou `"append"`

### `read_text`

Lit les textes (MTEXT + TEXT).

```json
{"actions":[{"type":"read_text","scope":"single"}]}
```

- **scope**:
  - `"single"`: retourne le texte si unique, sinon indique que ce n’est pas unique
  - `"all"`: retourne tous les textes

### `list_entities`

Retourne des compteurs.

```json
{"actions":[{"type":"list_entities","types":["mtext","circle"],"layer":"AI"}]}
```

- **types**: liste de fragments de nom de type (optionnel)
- **layer**: filtre calque (optionnel)

### `insert_block`

Insère un bloc et (optionnel) renseigne des attributs.

```json
{"actions":[{"type":"insert_block","name":"CARTOUCHE_A3","insert":{"x":0,"y":0},"scale":1,"rotation":0,"attributes":{"TITRE":"Test","DATE":"2026-04-15"}}]}
```

### `generate_lisp`

Écrit un fichier `.lsp` dans `(<workspace>)\generated\lisp\`.

```json
{"actions":[{"type":"generate_lisp","name":"select_red_text","code":"(defun c:SelTexteRouge () (princ))"}]}
```

