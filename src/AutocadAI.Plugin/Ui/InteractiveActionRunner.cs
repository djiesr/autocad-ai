using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Newtonsoft.Json.Linq;

namespace AutocadAI.Ui;

public static class InteractiveActionRunner
{
    // Supports:
    // - prompt (already handled by PrimitiveExecutor too, but we keep it here for sequencing)
    // - get_point {var:"p1", message:"Pick point"}
    // - get_text {var:"t1", message:"Enter text", allowSpaces:true}
    // - set_var {var:"x", value:...}
    // Then forwards the rest to PrimitiveExecutor with variable substitution.
    public static async Task<List<string>> RunAsync(Document doc, string workspaceRoot, JToken parsedJson)
    {
        var logs = new List<string>();
        var ctx = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

        var actions = ExtractActions(parsedJson);
        foreach (var action in actions)
        {
            if (action.Type != JTokenType.Object)
            {
                logs.Add("Action ignorée (format invalide).");
                continue;
            }

            var obj = (JObject)action;
            var type = (obj.Value<string>("type") ?? "").Trim().ToLowerInvariant();

            if (type == "prompt")
            {
                var msg = obj.Value<string>("message") ?? obj.Value<string>("text") ?? obj.Value<string>("content") ?? "";
                logs.Add("prompt: " + (string.IsNullOrWhiteSpace(msg) ? "(vide)" : msg));
                continue;
            }

            if (type == "set_var")
            {
                var v = (obj.Value<string>("var") ?? "").Trim();
                if (v.Length == 0) { logs.Add("set_var: var manquant."); continue; }
                ctx[v] = obj["value"] ?? JValue.CreateNull();
                logs.Add("set_var: OK " + v);
                continue;
            }

            if (type == "get_point")
            {
                var v = (obj.Value<string>("var") ?? "").Trim();
                if (v.Length == 0) v = "p";
                var msg = obj.Value<string>("message") ?? "Point:";
                var ppr = doc.Editor.GetPoint("\n" + msg + " ");
                if (ppr.Status != PromptStatus.OK)
                {
                    logs.Add("get_point: annulé.");
                    continue;
                }
                ctx[v] = new JObject { ["x"] = ppr.Value.X, ["y"] = ppr.Value.Y, ["z"] = ppr.Value.Z };
                logs.Add("get_point: OK " + v);
                continue;
            }

            if (type == "get_text")
            {
                var v = (obj.Value<string>("var") ?? "").Trim();
                if (v.Length == 0) v = "t";
                var msg = obj.Value<string>("message") ?? "Texte:";
                var pso = new PromptStringOptions("\n" + msg + " ")
                {
                    AllowSpaces = obj.Value<bool?>("allowSpaces") ?? true
                };
                var psr = doc.Editor.GetString(pso);
                if (psr.Status != PromptStatus.OK)
                {
                    logs.Add("get_text: annulé.");
                    continue;
                }
                ctx[v] = psr.StringResult ?? "";
                logs.Add("get_text: OK " + v);
                continue;
            }

            // Normal action: substitute variables then execute via PrimitiveExecutor
            var expanded = (JToken)obj.DeepClone();
            SubstituteVars(expanded, ctx);
            logs.AddRange(await AutocadAI.Primitives.PrimitiveExecutor.ExecuteAsync(doc, workspaceRoot, expanded).ConfigureAwait(true));
        }

        return logs;
    }

    private static List<JToken> ExtractActions(JToken parsedJson)
    {
        if (parsedJson.Type == JTokenType.Object && parsedJson["actions"] is JArray arr)
            return new List<JToken>(arr);
        return new List<JToken> { parsedJson };
    }

    private static void SubstituteVars(JToken token, Dictionary<string, JToken> ctx)
    {
        if (token.Type == JTokenType.String)
        {
            var s = token.Value<string>() ?? "";
            if (TryResolveVar(s, ctx, out var val))
                token.Replace(val);
            return;
        }

        if (token is JObject o)
        {
            foreach (var p in o.Properties())
                SubstituteVars(p.Value, ctx);
            return;
        }

        if (token is JArray a)
        {
            for (var i = 0; i < a.Count; i++)
                SubstituteVars(a[i], ctx);
        }
    }

    private static bool TryResolveVar(string s, Dictionary<string, JToken> ctx, out JToken value)
    {
        value = JValue.CreateNull();
        s = (s ?? "").Trim();
        // Accept "$p1" / "$p1.x" and also "p1" / "p1.x" (models often omit '$')
        var path = s.StartsWith("$", StringComparison.Ordinal) ? s.Substring(1) : s;
        if (path.Length == 0) return false;

        var dot = path.IndexOf('.');
        var key = dot >= 0 ? path.Substring(0, dot) : path;
        if (!ctx.TryGetValue(key, out var v)) return false;

        if (dot < 0)
        {
            value = v.DeepClone();
            return true;
        }

        var field = path.Substring(dot + 1);
        if (v is JObject o && o[field] != null)
        {
            value = o[field]!.DeepClone();
            return true;
        }

        return false;
    }
}

