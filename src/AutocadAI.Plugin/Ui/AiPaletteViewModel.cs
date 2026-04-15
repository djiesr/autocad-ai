using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using System.Windows.Input;
using AutocadAI.Actions;
using Newtonsoft.Json.Linq;
using AutocadAI;
using AutocadAI.Primitives;

namespace AutocadAI.Ui;

public sealed class AiPaletteViewModel : INotifyPropertyChanged
{
    private readonly Action<string> _log;
    private static readonly HttpClient Http = new HttpClient();

    private string _workspaceRoot = "";
    private string _projectConfigPath = "";
    private string _policyLevel = "";
    private string _preferredEngine = "";
    private string _localEndpoint = "";
    private string _localModel = "";
    private string _cloudProvider = "anthropic";
    private bool _settingsOpen;
    private bool _enableAiLogs = true;
    private bool _enableWebSearch = true;
    private string _interactionMode = "autocad";
    private bool _isBusy;
    private readonly List<(string Role, string Content)> _history = new List<(string Role, string Content)>();
    private const int MaxHistoryTurns = 8; // 8 paires user+assistant max

    private string _transcript = "";
    private string _prompt = "";

    public AiPaletteViewModel(Action<string> log)
    {
        _log = log;
        SendCommand = new RelayCommand(() => _ = SendAsync(), () => !string.IsNullOrWhiteSpace(Prompt) && !IsBusy);
        ToggleSettingsCommand = new RelayCommand(() => SettingsOpen = !SettingsOpen);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ClearCommand = new RelayCommand(ClearConversation);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set { _workspaceRoot = value; OnPropertyChanged(); }
    }

    public string ProjectConfigPath
    {
        get => _projectConfigPath;
        set { _projectConfigPath = value; OnPropertyChanged(); }
    }

    public string PolicyLevel
    {
        get => _policyLevel;
        set { _policyLevel = value; OnPropertyChanged(); }
    }

    public string PreferredEngine
    {
        get => _preferredEngine;
        set { _preferredEngine = value; OnPropertyChanged(); }
    }

    public string LocalEndpoint
    {
        get => _localEndpoint;
        set { _localEndpoint = value; OnPropertyChanged(); }
    }

    public string LocalModel
    {
        get => _localModel;
        set { _localModel = value; OnPropertyChanged(); }
    }

    public string CloudProvider
    {
        get => _cloudProvider;
        set { _cloudProvider = value; OnPropertyChanged(); }
    }

    public bool EnableAiLogs
    {
        get => _enableAiLogs;
        set { _enableAiLogs = value; OnPropertyChanged(); }
    }

    public bool EnableWebSearch
    {
        get => _enableWebSearch;
        set { _enableWebSearch = value; OnPropertyChanged(); }
    }

    public string InteractionMode
    {
        get => _interactionMode;
        set { _interactionMode = (value ?? "autocad").Trim().ToLowerInvariant(); OnPropertyChanged(); }
    }

    public bool SettingsOpen
    {
        get => _settingsOpen;
        set { _settingsOpen = value; OnPropertyChanged(); }
    }

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

    public string Transcript
    {
        get => _transcript;
        set { _transcript = value; OnPropertyChanged(); }
    }

    public string Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            OnPropertyChanged();
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand SendCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ClearCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AppendLine(string text)
    {
        var sb = new StringBuilder(Transcript ?? "");
        if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
            sb.AppendLine();
        sb.AppendLine(text);
        Transcript = sb.ToString();
    }

    private async Task SendAsync()
    {
        IsBusy = true;
        var text = Prompt.Trim();
        Prompt = "";

        AppendLine("Vous: " + text);
        _log(text);
        LogToFile("USER", text);

        try
        {
            // Validation politique local-only (#9)
            if (PolicyLevel == "local-only")
            {
                try
                {
                    var uri = new Uri(LocalEndpoint);
                    if (!uri.IsLoopback)
                    {
                        AppendLine("ERREUR: PolicyLevel ‘local-only’ mais l’endpoint n’est pas local (" + LocalEndpoint + ").");
                        return;
                    }
                }
                catch (UriFormatException)
                {
                    AppendLine("ERREUR: LocalEndpoint invalide : " + LocalEndpoint);
                    return;
                }
            }

            AppendLine("IA: (en cours…)");
            var reply = await CallLmStudioAsync(text);
            LogToFile("MODEL_RAW", reply);

            // Historique : on enregistre le tour courant pour les prochaines requêtes (#5)
            AddToHistory("user", text);
            AddToHistory("assistant", reply);

            // Mode général: si le modèle renvoie un web_search, on l’exécute puis on reformule.
            if (InteractionMode == "general" && await TryExecuteGeneralWebSearchFlowAsync(reply, text))
                return;

            // Si on demande une action liée au texte mais que le modèle répond en prose,
            // on relance une requête "strict JSON" en incluant sa réponse comme contenu.
            if (!LooksLikeJson(reply) && LooksLikeMTextIntent(text))
            {
                AppendLine("IA: (conversion en actions…)");
                var retry = await CallLmStudioStrictJsonAsync(text, reply);
                LogToFile("MODEL_RAW_RETRY", retry);
                reply = retry;
            }

            if (await TryExecuteJsonAsync(reply))
                return;

            // Si pas JSON, on affiche le texte (utile pour les réponses "conseil")
            AppendLine("IA: " + reply);
        }
        catch (Exception ex)
        {
            AppendLine("IA: Erreur LM Studio: " + ex.Message);
            AppendLine("IA: Vérifie que LM Studio a démarré le serveur ‘OpenAI Compatible’ et que LocalEndpoint est correct.");
            LogToFile("ERROR", ex.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> TryExecuteGeneralWebSearchFlowAsync(string reply, string originalUserText)
    {
        var trimmed = (reply ?? "").Trim();
        if (trimmed.Length == 0) return false;

        // Accept either strict {"actions":[{"type":"web_search",...}]}
        // or loose {"action":"web_search",...}
        JToken tok;
        try { tok = JToken.Parse(trimmed); }
        catch { return false; }

        var q = "";
        var max = 5;

        if (tok.Type == JTokenType.Object)
        {
            if (tok["actions"] is JArray arr && arr.Count > 0 && arr[0]?["type"]?.ToString() == "web_search")
            {
                q = arr[0]?["query"]?.ToString() ?? arr[0]?["q"]?.ToString() ?? "";
                max = arr[0]?["max_results"]?.Value<int?>() ?? 5;
            }
            else if (tok["action"]?.ToString() == "web_search")
            {
                q = tok["query"]?.ToString() ?? tok["q"]?.ToString() ?? "";
                max = tok["max_results"]?.Value<int?>() ?? 5;
            }
        }

        if (string.IsNullOrWhiteSpace(q))
            return false;

        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return false;

        var action = new JObject
        {
            ["type"] = "web_search",
            ["query"] = q,
            ["max_results"] = max
        };

        AppendLine("IA: (web_search) exécution…");
        // Await directement : pas de Task.Run, on reste sur le thread document tout au long.
        var logs = await InteractiveActionRunner.RunAsync(doc, WorkspaceRoot, new JObject { ["actions"] = new JArray(action) }).ConfigureAwait(true);
        foreach (var l in logs)
        {
            AppendLine("AutoCAD: " + l);
            LogToFile("ACTIONS", l);
        }

        // Reformulation : await direct depuis le thread document — AppendLine thread-safe.
        var resultsJson = logs.FirstOrDefault(x => x.StartsWith("web_search:", StringComparison.OrdinalIgnoreCase)) ?? "";
        try
        {
            var final = await CallLmStudioGeneralAnswerFromSearchAsync(originalUserText, q, resultsJson);
            AppendLine("IA: " + final);
            LogToFile("MODEL_FINAL", final);
        }
        catch (Exception ex)
        {
            AppendLine("IA: Erreur reformulation: " + ex.Message);
            LogToFile("ERROR", ex.ToString());
        }

        return true;
    }

    private async Task<string> CallLmStudioGeneralAnswerFromSearchAsync(string userText, string query, string resultsLine)
    {
        var sys =
            "Tu réponds en français. Tu dois répondre à la question en utilisant les résultats fournis." +
            " Donne une liste claire et ajoute 2-5 sources (URLs) issues des résultats." +
            " N’invente pas de faits non présents.";

        var payload = new JObject
        {
            ["model"]       = LocalModel,
            ["messages"]    = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = sys },
                new JObject { ["role"] = "user",   ["content"] = "Question: " + userText + "\nRequête: " + query + "\nRésultats (JSON):\n" + resultsLine }
            },
            ["temperature"] = 0.2
        }.ToString(Newtonsoft.Json.Formatting.None);

        using var req = new HttpRequestMessage(HttpMethod.Post, LocalEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var resp = await Http.SendAsync(req).ConfigureAwait(true);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");

        var content = ExtractChoiceContent(body);
        return string.IsNullOrWhiteSpace(content) ? body : StripThink(content).Trim();
    }

    private static bool LooksLikeJson(string s)
    {
        var t = (s ?? "").TrimStart();
        return t.StartsWith("{") || t.StartsWith("[");
    }

    private static bool LooksLikeMTextIntent(string userText)
    {
        var t = (userText ?? "").ToLowerInvariant();
        return t.Contains("mtext") || (t.Contains("texte") && (t.Contains("écrit") || t.Contains("ecrit") || t.Contains("insère") || t.Contains("insere") || t.Contains("mets")));
    }

    private async Task<string> CallLmStudioStrictJsonAsync(string userText, string modelText)
    {
        var strictSystem =
            "Tu dois renvoyer UNIQUEMENT un JSON {\"actions\":[...]}." +
            " Objectif: exécuter la demande dans AutoCAD via primitives." +
            " Si tu veux écrire du texte, utilise create_mtext (insert:{x,y} requis) ou update_single_mtext si approprié." +
            " Ne renvoie aucun texte hors JSON.";

        var payload = new JObject
        {
            ["model"]       = LocalModel,
            ["messages"]    = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = strictSystem },
                new JObject { ["role"] = "user",   ["content"] = "Demande: " + userText + "\nRéponse précédente (à convertir en actions):\n" + modelText }
            },
            ["temperature"] = 0.0
        }.ToString(Newtonsoft.Json.Formatting.None);

        using var req = new HttpRequestMessage(HttpMethod.Post, LocalEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var resp = await Http.SendAsync(req).ConfigureAwait(true);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");

        var content = ExtractChoiceContent(body);
        return string.IsNullOrWhiteSpace(content) ? body : StripThink(content).Trim();
    }

    private void SaveSettings()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ProjectConfigPath))
            {
                AppendLine("AutoCAD: Impossible de sauvegarder (ProjectConfigPath vide).");
                return;
            }

            var s = new ProjectSettings
            {
                PolicyLevel = PolicyLevel,
                PreferredEngine = PreferredEngine,
                LocalEndpoint = LocalEndpoint,
                LocalModel = LocalModel,
                CloudProvider = CloudProvider,
                EnableAiLogs = EnableAiLogs,
                EnableWebSearch = EnableWebSearch,
                InteractionMode = InteractionMode
            };
            s.Save(ProjectConfigPath);
            AppendLine("AutoCAD: Paramètres sauvegardés.");
            LogToFile("SETTINGS", "Saved project.json");
        }
        catch (Exception ex)
        {
            AppendLine("AutoCAD: Erreur sauvegarde paramètres: " + ex.Message);
        }
    }

    private async Task<bool> TryExecuteJsonAsync(string modelReply)
    {
        var trimmed = (modelReply ?? "").Trim();
        if (trimmed.Length == 0)
            return false;

        // Accept JSON inside ``` fences too
        if (trimmed.StartsWith("```"))
        {
            var first = trimmed.IndexOf('\n');
            var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first)
                trimmed = trimmed.Substring(first + 1, last - first - 1).Trim();
        }

        if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
            return false;

        JToken parsed;
        try
        {
            parsed = JToken.Parse(trimmed);
        }
        catch
        {
            return false;
        }

        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
        {
            AppendLine("AutoCAD: Aucun document actif.");
            return true;
        }

        AppendLine("IA: (actions) exécution…");
        var logs = await InteractiveActionRunner.RunAsync(doc, WorkspaceRoot, parsed).ConfigureAwait(true);
        foreach (var l in logs)
        {
            AppendLine("AutoCAD: " + l);
            LogToFile("ACTIONS", l);
        }

        return true;
    }

    private void LogToFile(string category, string message)
    {
        if (!EnableAiLogs) return;
        if (string.IsNullOrWhiteSpace(WorkspaceRoot)) return;

        try
        {
            var dir = Path.Combine(WorkspaceRoot, "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"ai_{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}".Replace("\r\n", "\n").Replace('\r', '\n');
            File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // never crash UI because of logging
        }
    }

    private async Task<string> CallLmStudioAsync(string userText)
    {
        if (string.IsNullOrWhiteSpace(LocalEndpoint))
            throw new InvalidOperationException("LocalEndpoint est vide (project.json).");
        if (string.IsNullOrWhiteSpace(LocalModel))
            throw new InvalidOperationException("LocalModel est vide (project.json).");

        var systemPrompt = InteractionMode == "general"
            ? BuildGeneralSystemPrompt()
            : BuildAutocadSystemPrompt();

        // Construction du payload avec Newtonsoft.Json — échappement correct de tous les caractères (#6)
        var messages = new JArray();
        messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

        // Inclure l'historique de conversation (#5) : N derniers tours
        var historyStart = Math.Max(0, _history.Count - MaxHistoryTurns * 2);
        for (var i = historyStart; i < _history.Count; i++)
            messages.Add(new JObject { ["role"] = _history[i].Role, ["content"] = _history[i].Content });

        messages.Add(new JObject { ["role"] = "user", ["content"] = userText });

        var payload = new JObject
        {
            ["model"]       = LocalModel,
            ["messages"]    = messages,
            ["temperature"] = 0.2
        }.ToString(Newtonsoft.Json.Formatting.None);

        using var req = new HttpRequestMessage(HttpMethod.Post, LocalEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var resp = await Http.SendAsync(req).ConfigureAwait(true);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");

        var content = ExtractChoiceContent(body);
        if (string.IsNullOrWhiteSpace(content))
            return body;

        content = StripThink(content).Trim();
        if (content.StartsWith("(b)", StringComparison.OrdinalIgnoreCase))
            content = content.Substring(3).TrimStart();
        return content;
    }

    private static string BuildAutocadSystemPrompt()
    {
        return
            "Tu es l’assistant du plugin AutoCAD AI. Réponds TOUJOURS soit (A) un JSON d’actions AutoCAD, soit (B) du texte (si aucune action).\n\n" +
            "Format JSON attendu: {\"actions\":[{...}]}. JSON STRICT: les champs sont des paires clé/valeur (pas de 'radius=5'), les listes sont des tableaux (pas de texte), ex: \"points\":[[0,0],[10,0]].\n\n" +
            "Types supportés: get_drawing_info, get_variable, set_variable, list_layers, list_blocks, list_layouts, web_search, select_entities, get_selection, get_entity_properties, get_bounding_box, get_drawing_extents, move_entity, rotate_entity, scale_entity, delete_entity, change_entity_properties, read_block_attributes, update_block_attributes, ensure_layer, set_current_layer, draw_line, draw_polyline, draw_rectangle, draw_circle, create_text, create_mtext, update_single_mtext, read_text, list_entities, insert_block, create_layout, copy_layout, set_current_layout, generate_lisp, prompt, get_point, get_text, set_var.\n\n" +
            "Layouts: pour créer un layout, utilise create_layout avec un nom explicite, ex: {\"type\":\"create_layout\",\"name\":\"Page 9\"}. Pour activer: {\"type\":\"set_current_layout\",\"name\":\"Page 9\"}.\n\n" +
            "Si l'utilisateur mentionne un layout modèle (ex: \"A1-imperial\"), NE PAS utiliser create_layout: utilise copy_layout, ex: {\"type\":\"copy_layout\",\"from\":\"A1-imperial\",\"to\":\"Page 1\"}.\n\n" +
            "Règles: tu PEUX lire le contenu du dessin via read_text ou get_entity_properties. Ne dis jamais que tu ne peux pas lire/traiter le fichier. N’utilise pas de smileys/emoji.\n" +
            "IMPORTANT: n’utilise insert_block QUE si l’utilisateur fournit un nom de bloc existant; sinon dessine avec draw_polyline/draw_circle.\n" +
            "Si tu as besoin d’un point, utilise get_point {var:\"p1\", message:\"...\"} puis réutilise avec $p1.x/$p1.y.\n" +
            "Si tu as besoin d’un texte, utilise get_text {var:\"t1\", message:\"...\"} puis réutilise $t1.\n";
    }

    private static string BuildGeneralSystemPrompt()
    {
        return
            "Tu réponds en français, de manière concise et utile.\n" +
            "Tu peux utiliser web_search quand la question est factuelle et que des sources seraient utiles.\n" +
            "Si tu utilises web_search, renvoie d’abord un JSON d’action web_search, puis après exécution, réponds en texte avec une liste + sources.\n" +
            "N’utilise pas de smileys/emoji.";
    }

    private static string ExtractChoiceContent(string json)
    {
        // Parser fiable via Newtonsoft.Json (#7) — gère les réponses imbriquées sans ambiguïté.
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

    private static string StripThink(string content)
    {
        // Many local models emit <think>...</think>. We hide it in the UI.
        var start = content.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return content.Trim();

        var end = content.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return content.Trim();

        var after = content.Substring(end + "</think>".Length);
        return after.Trim();
    }

    private void ClearConversation()
    {
        Transcript = "";
        _history.Clear();
    }

    private void AddToHistory(string role, string content)
    {
        _history.Add((role, content));
        // Garder MaxHistoryTurns paires user+assistant pour ne pas dépasser le context window
        while (_history.Count > MaxHistoryTurns * 2)
            _history.RemoveAt(0);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

