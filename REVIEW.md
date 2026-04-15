# AutoCAD AI Plugin — Revue de code

> Généré le 2026-04-15. Les numéros de ligne sont indicatifs (basés sur l'état actuel du code).

---

## Tableau de bord

| # | Priorité | Fichier | Problème | Statut |
|---|----------|---------|----------|--------|
| 1 | 🔴 Critique | `PrimitiveExecutor.cs:169` | Deadlock sync-over-async WebSearch | ✅ Corrigé |
| 2 | 🔴 Critique | `AiPaletteViewModel.cs:246` | `AppendLine` depuis thread non-UI | ✅ Corrigé |
| 3 | 🟠 Important | `AiPaletteViewModel.cs:144` | `async void Send()` | ✅ Corrigé |
| 4 | 🟠 Important | `AiPaletteViewModel.cs` | Pas de verrou pendant l'envoi (double-send) | ✅ Corrigé |
| 5 | 🟠 Important | `AiPaletteViewModel.cs` | Pas d'historique de conversation envoyé au LLM | ✅ Corrigé |
| 6 | 🟡 Moyen | `AiPaletteViewModel.cs:268` | JSON construit par concaténation de strings | ✅ Corrigé |
| 7 | 🟡 Moyen | `AiPaletteViewModel.cs:498` | `ExtractChoiceContent` parser maison fragile | ✅ Corrigé |
| 8 | 🟡 Moyen | `PrimitiveExecutor.cs:299` | `set_variable` sans liste blanche | ✅ Corrigé |
| 9 | 🟡 Moyen | `ProjectSettings.cs` / `AiPaletteViewModel.cs` | `PolicyLevel local-only` non validé | ✅ Corrigé |
| 10 | 🟢 Mineur | `ProjectSettings.cs:62` | `ToPrettyJson()` retourne du JSON minifié | ✅ Corrigé |
| 11 | 🟢 Mineur | `PrimitiveExecutor.cs:531` | Code mort dans `GetDrawingExtents` | ✅ Corrigé |
| 12 | 🟢 Mineur | `AcadActions.cs:213` | `ReadSingleTextIfUnambiguous` retourne `""` ambigu | ✅ Corrigé |
| 13 | 🟢 Mineur | `Web/DuckDuckGoSearch.cs` | HTML scraping fragile (dépend de la structure DDG) | À évaluer |

---

## Détail des problèmes

---

### #1 🔴 Deadlock : sync-over-async dans `WebSearch`

**Fichier :** `src/AutocadAI.Plugin/Primitives/PrimitiveExecutor.cs` ~ligne 169

**Problème :**

```csharp
// DANGEREUX
var arr = DuckDuckGoSearch.SearchAsync(query, max).GetAwaiter().GetResult();
```

`PrimitiveExecutor.Execute` est appelé depuis `InteractiveActionRunner.Run`, lui-même appelé
depuis `AiPaletteViewModel.TryExecuteJson` — sur le `SynchronizationContext` WPF (thread UI).

`.GetResult()` bloque ce thread. `SearchAsync` utilise `ConfigureAwait(false)` à l'intérieur,
mais si un seul await intermédiaire n'a pas `ConfigureAwait(false)`, la continuation essaie de
revenir sur le thread UI qui est bloqué → **deadlock**.

Même sans deadlock, bloquer le thread UI pendant une requête HTTP (0 à ~5 s) gèle AutoCAD
complètement.

**Correction :**

Rendre `ExecuteOne` et `Execute` asynchrones (`Task<List<string>>`), propager l'`await` jusqu'à
`InteractiveActionRunner.Run` puis jusqu'à `TryExecuteJson` (déjà dans un contexte async).

```csharp
// PrimitiveExecutor
private static async Task<string> WebSearchAsync(JObject a)
{
    var arr = await DuckDuckGoSearch.SearchAsync(query, max).ConfigureAwait(false);
    return "web_search: " + arr.ToString(Newtonsoft.Json.Formatting.None);
}

// InteractiveActionRunner
public static async Task<List<string>> RunAsync(Document doc, string workspaceRoot, JToken parsedJson)

// AiPaletteViewModel
var logs = await InteractiveActionRunner.RunAsync(doc, WorkspaceRoot, parsed);
```

---

### #2 🔴 `AppendLine` depuis un thread de fond

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs` ~ligne 246

**Problème :**

```csharp
_ = Task.Run(async () =>
{
    var final = await CallLmStudioGeneralAnswerFromSearchAsync(...);
    AppendLine("IA: " + final);   // <-- thread du pool, pas le thread UI !
    LogToFile("MODEL_FINAL", final);
});
```

`AppendLine` modifie la propriété `Transcript` (un `string` bindé WPF). WPF lève une
`InvalidOperationException` ("The calling thread cannot access this object because a different
thread owns it") ou corrompt silencieusement l'état selon la version.

**Correction :**

```csharp
_ = Task.Run(async () =>
{
    var final = await CallLmStudioGeneralAnswerFromSearchAsync(...).ConfigureAwait(false);
    // Marshaler vers le thread UI
    Autodesk.AutoCAD.ApplicationServices.Application.Invoke(() =>
    {
        AppendLine("IA: " + final);
        LogToFile("MODEL_FINAL", final);
    });
});
```

> Note : dans un contexte AutoCAD, `Application.Invoke` est préférable à
> `Dispatcher.Invoke` car il garantit l'exécution sur le thread du document.

---

### #3 🟠 `async void Send()`

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs` ~ligne 144

**Problème :**

```csharp
private async void Send()   // <-- async void
```

`async void` ne peut pas être attendu. Si une exception est levée **après** un `await`
sur un thread sans `SynchronizationContext` (ex. : continuation sur le pool après un
`ConfigureAwait(false)`), elle n'est pas capturée par le `try/catch` encapsulant et
**crashe le processus** (AppDomain.UnhandledException).

Le `try/catch` présent ne protège que le chemin synchrone et les `await` qui restent
sur le thread UI.

**Correction :**

```csharp
// Dans RelayCommand : wrapper sync → async Task
private async Task SendAsync()
{
    // ... même code ...
}

// Dans le constructeur :
SendCommand = new RelayCommand(
    () => _ = SendAsync(),
    () => !string.IsNullOrWhiteSpace(Prompt)
);
```

Ou mieux, utiliser un `AsyncRelayCommand` qui gère les exceptions proprement.

---

### #4 🟠 Pas de verrou pendant l'envoi (double-send)

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs`

**Problème :**

L'utilisateur peut appuyer plusieurs fois sur Envoyer pendant qu'une requête est en cours.
Chaque `Send()` lance une nouvelle requête HTTP indépendante. Les réponses arrivent dans un
ordre non déterministe et s'entremêlent dans le transcript.

Il n'existe pas de `CancellationToken` pour annuler la requête en cours.

**Correction :**

```csharp
private bool _isBusy;

public bool IsBusy
{
    get => _isBusy;
    private set
    {
        _isBusy = value;
        OnPropertyChanged();
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

// SendCommand CanExecute :
() => !string.IsNullOrWhiteSpace(Prompt) && !IsBusy

// Dans SendAsync() :
IsBusy = true;
try { /* ... */ }
finally { IsBusy = false; }
```

Ajouter aussi un `CancellationTokenSource` pour annuler la requête en cours si
l'utilisateur ferme la palette ou envoie un nouveau message.

---

### #5 🟠 Pas d'historique de conversation envoyé au LLM

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs` ~lignes 440-448

**Problème :**

Chaque requête envoie uniquement `[system, user_message]` :

```csharp
"\"messages\":["
+ "{\"role\":\"system\",\"content\":" + Json(systemPrompt) + "},"
+ "{\"role\":\"user\",\"content\":" + Json(userText) + "}"
+ "],"
```

Le modèle n'a aucun accès aux échanges précédents. Si l'utilisateur demande
"modifie ce que tu viens de dessiner", le LLM ne sait pas quoi modifier.
Le transcript affiché est trompeur : il ressemble à une conversation, mais
le LLM commence chaque tour de zéro.

**Correction :**

Maintenir une liste `List<(string role, string content)>` dans le ViewModel,
ajouter chaque tour (`user` + `assistant`), et l'envoyer à chaque requête.
Prévoir une limite (ex. : 10 derniers tours) pour éviter de dépasser le
context window.

```csharp
private readonly List<(string Role, string Content)> _history = new();

// Dans SendAsync() après réception :
_history.Add(("user", text));
_history.Add(("assistant", reply));

// Dans CallLmStudioAsync() : inclure _history dans les messages
```

---

### #6 🟡 JSON construit par concaténation de strings

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs` ~lignes 268, 310, 440

**Problème :**

```csharp
var payload =
    "{"
    + "\"model\":" + Json(LocalModel) + ","
    + "\"messages\":["
    + "{\"role\":\"system\",\"content\":" + Json(systemPrompt) + "},"
    ...
```

La méthode `Json()` manuelle ne gère pas les caractères Unicode (`\u0000`),
ni `\b`, `\f`, ni les surrogates. Un nom de modèle ou un prompt contenant
ces caractères produit un JSON invalide rejeté par LM Studio.

Newtonsoft.Json est déjà une dépendance du projet — autant l'utiliser.

**Correction :**

```csharp
var requestObj = new JObject
{
    ["model"] = LocalModel,
    ["temperature"] = 0.2,
    ["messages"] = new JArray
    {
        new JObject { ["role"] = "system", ["content"] = systemPrompt },
        new JObject { ["role"] = "user",   ["content"] = userText }
    }
};
var payload = requestObj.ToString(Newtonsoft.Json.Formatting.None);
```

---

### #7 🟡 `ExtractChoiceContent` — parser maison fragile

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs` ~ligne 498

**Problème :**

```csharp
var keyPos = json.IndexOf("\"content\"", StringComparison.OrdinalIgnoreCase);
```

Ce parser cherche la **première** occurrence de `"content"` dans la réponse JSON.
Si la réponse contient un tool call, des métadonnées, ou si un message précédent
dans les `messages` de la réponse contient `"content"`, le parser retourne
la mauvaise valeur.

**Correction :**

```csharp
private static string ExtractChoiceContent(string json)
{
    try
    {
        var root = JToken.Parse(json);
        return root["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? "";
    }
    catch
    {
        return "";
    }
}
```

---

### #8 🟡 `set_variable` sans liste blanche

**Fichier :** `src/AutocadAI.Plugin/Primitives/PrimitiveExecutor.cs` ~ligne 299

**Problème :**

```csharp
Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable(name, JTokenToClr(v));
```

Aucune validation du nom de la variable. Un LLM mal guidé (ou un prompt
injecté via web_search) peut appeler :

- `set_variable {name:"FILEDIA", value:0}` → bloque toutes les dialogues fichiers
- `set_variable {name:"CMDECHO", value:0}` → masque toutes les commandes dans la console
- `set_variable {name:"USERS1", value:"..."}` → variables utilisateur arbitraires

**Correction :**

```csharp
private static readonly HashSet<string> AllowedVariables = new(StringComparer.OrdinalIgnoreCase)
{
    "LTSCALE", "DIMSCALE", "TEXTSIZE", "INSUNITS",
    "OSMODE", "ORTHOMODE", "SNAPMODE", "GRIDMODE",
    "CELTYPE", "CECOLOR", "CLAYER"
    // Ajouter selon les besoins
};

private static string SetVariable(JObject a)
{
    var name = (a.Value<string>("name") ?? "").Trim().ToUpperInvariant();
    if (!AllowedVariables.Contains(name))
        return $"set_variable: variable '{name}' non autorisée.";
    // ...
}
```

---

### #9 🟡 `CloudProvider` / `PolicyLevel` — paramètres fantômes

**Fichier :** `src/AutocadAI.Plugin/Ui/AiPaletteViewModel.cs`

**Problème :**

Les paramètres `CloudProvider`, `PolicyLevel`, et `PreferredEngine` sont sauvegardés
dans `project.json` et affichés dans l'UI, mais ne sont **jamais lus** dans la logique
d'envoi. `CallLmStudioAsync` est toujours appelé, quelle que soit la valeur de ces
paramètres.

Un utilisateur configurant `PolicyLevel = "local-only"` n'a aucune protection réelle :
si `LocalEndpoint` pointe vers un serveur distant, les données partent quand même.

**Correction :**

Au minimum, ajouter une validation au moment de l'envoi :
```csharp
if (PolicyLevel == "local-only")
{
    var uri = new Uri(LocalEndpoint);
    if (!uri.IsLoopback)
    {
        AppendLine("ERREUR: PolicyLevel 'local-only' mais endpoint non local.");
        return;
    }
}
```

Pour `PreferredEngine = "cloud"`, implémenter un vrai appel API cloud (Anthropic, OpenAI)
ou retirer l'option de l'UI.

---

### #10 🟢 `ToPrettyJson()` retourne du JSON minifié

**Fichier :** `src/AutocadAI.Plugin/ProjectSettings.cs` ~ligne 62

**Problème :**

`DataContractJsonSerializer` ne supporte pas l'indentation JSON.
La méthode s'appelle `ToPrettyJson` mais retourne une ligne compacte.

**Correction :**

```csharp
public string ToPrettyJson()
{
    // Sérialiser avec DataContractJsonSerializer puis reformater avec Newtonsoft
    using var ms = new MemoryStream();
    var ser = new DataContractJsonSerializer(typeof(ProjectSettings));
    ser.WriteObject(ms, this);
    var raw = Encoding.UTF8.GetString(ms.ToArray());
    return JToken.Parse(raw).ToString(Newtonsoft.Json.Formatting.Indented);
}
```

---

### #11 🟢 Code mort dans `GetDrawingExtents`

**Fichier :** `src/AutocadAI.Plugin/Primitives/PrimitiveExecutor.cs` ~ligne 531

**Problème :**

```csharp
try
{
    var ext = db.Extmin; // not always meaningful; prefer drawing extents via bounds
}
catch { }
```

`ext` est assignée et immédiatement abandonnée. Le commentaire lui-même indique
que ce code n'est pas utile.

**Correction :** Supprimer ce bloc.

---

### #12 🟢 `ReadSingleTextIfUnambiguous` retourne `""` de façon ambiguë

**Fichier :** `src/AutocadAI.Plugin/Actions/AcadActions.cs` ~ligne 213

**Problème :**

```csharp
if (count > 1 || only == null)
    return "";  // <-- impossible à distinguer d'un texte vide
```

Le cas "plusieurs textes" retourne `""`, indiscernable d'un texte vide ou d'une
erreur silencieuse.

**Correction :**

```csharp
if (count > 1)
    return null;  // ou une constante dédiée, selon l'usage attendu
```

---

### #13 🟢 DuckDuckGo HTML scraping

**Fichier :** `src/AutocadAI.Plugin/Web/DuckDuckGoSearch.cs`

**Problème :**

Le parser regex dépend des classes CSS `result__a` et `result__snippet` de DuckDuckGo.
DuckDuckGo change régulièrement son HTML ; ce scraper cassera silencieusement
(retournera 0 résultats sans erreur).

**Pistes :**

- DuckDuckGo Instant Answer API (`api.duckduckgo.com/?q=...&format=json`) — gratuite,
  JSON, stable, mais retourne surtout des résultats "instant" (Wikipedia, etc.)
- Brave Search API — gratuite jusqu'à 2 000 req/mois, JSON structuré
- SerpAPI / ScaleSerp — payant mais fiable

À court terme, ajouter un test de sanité : si `linkMatches.Count == 0`, logguer
un warning "DDG HTML structure changed?" plutôt que retourner silencieusement `[]`.

---

## Ce qui est bien — à ne pas casser

- `NormalizeActionObject` : très robuste face aux sorties LLM malformées.
- Système de variables `$p1` / `$p1.x` dans `InteractiveActionRunner`.
- `doc.LockDocument()` systématique dans toutes les opérations d'écriture.
- `StripThink` pour les balises `<think>` des modèles locaux.
- Retry en JSON strict (`CallLmStudioStrictJsonAsync`) quand le modèle répond en prose.
- `ProjectPaths` avec fallback `MyDocuments` pour les dessins non sauvegardés.
- Transactions correctement committées dans chaque méthode atomique.
