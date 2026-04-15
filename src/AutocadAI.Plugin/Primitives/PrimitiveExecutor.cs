using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
using Newtonsoft.Json.Linq;
using AutocadAI.Web;

namespace AutocadAI.Primitives;

public static class PrimitiveExecutor
{
    // Variables système AutoCAD autorisées pour set_variable (#8).
    // Liste blanche explicite : évite qu'un prompt injecté ne modifie FILEDIA, CMDECHO, etc.
    private static readonly HashSet<string> AllowedSetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LTSCALE", "DIMSCALE", "TEXTSIZE", "INSUNITS", "MEASUREMENT",
        "OSMODE", "ORTHOMODE", "SNAPMODE", "GRIDMODE", "POLARMODE",
        "CELTYPE", "CECOLOR", "CLAYER", "CELTSCALE",
        "DIMSTYLE", "TEXTSTYLE",
        "AUNITS", "AUPREC", "LUNITS", "LUPREC",
        "ANGBASE", "ANGDIR",
    };

    public static async Task<List<string>> ExecuteAsync(Document doc, string workspaceRoot, JToken? parsedJson)
    {
        var logs = new List<string>();
        if (parsedJson == null)
        {
            logs.Add("Aucune action (JSON vide).");
            return logs;
        }

        // Accept either:
        // { "actions": [ {...}, {...} ] }
        // or a single action object { "type": "...", ... }
        if (parsedJson.Type == JTokenType.Object && parsedJson["actions"] is JArray arr)
        {
            foreach (var a in arr)
                logs.AddRange(await ExecuteOneAsync(doc, workspaceRoot, a).ConfigureAwait(true));
            return logs;
        }

        logs.AddRange(await ExecuteOneAsync(doc, workspaceRoot, parsedJson).ConfigureAwait(true));
        return logs;
    }

    private static async Task<IEnumerable<string>> ExecuteOneAsync(Document doc, string workspaceRoot, JToken actionObj)
    {
        var logs = new List<string>();
        if (actionObj.Type != JTokenType.Object)
        {
            logs.Add("Action ignorée (format invalide).");
            return logs;
        }

        var a = (JObject)actionObj;
        NormalizeActionObject(a, logs);
        var type = (a.Value<string>("type") ?? "").ToLowerInvariant();
        switch (type)
        {
            case "get_drawing_info":
                logs.Add(GetDrawingInfo(doc));
                break;
            case "get_variable":
                logs.Add(GetVariable(a));
                break;
            case "set_variable":
                logs.Add(SetVariable(a));
                break;
            case "list_layers":
                logs.Add(ListLayers(doc));
                break;
            case "list_blocks":
                logs.Add(ListBlocks(doc));
                break;
            case "list_layouts":
                logs.Add(ListLayouts(doc));
                break;
            case "web_search":
                logs.Add(await WebSearchAsync(a).ConfigureAwait(true));
                break;
            case "select_entities":
                logs.Add(SelectEntities(doc, a));
                break;
            case "get_selection":
                logs.Add(GetSelection(doc));
                break;
            case "get_entity_properties":
                logs.Add(GetEntityProperties(doc, a));
                break;
            case "get_bounding_box":
                logs.Add(GetBoundingBox(doc, a));
                break;
            case "get_drawing_extents":
                logs.Add(GetDrawingExtents(doc));
                break;
            case "move_entity":
                logs.Add(MoveEntity(doc, a));
                break;
            case "rotate_entity":
                logs.Add(RotateEntity(doc, a));
                break;
            case "scale_entity":
                logs.Add(ScaleEntity(doc, a));
                break;
            case "delete_entity":
                logs.Add(DeleteEntity(doc, a));
                break;
            case "change_entity_properties":
                logs.Add(ChangeEntityProperties(doc, a));
                break;
            case "read_block_attributes":
                logs.Add(ReadBlockAttributes(doc, a));
                break;
            case "update_block_attributes":
                logs.Add(UpdateBlockAttributes(doc, a));
                break;
            case "ensure_layer":
                logs.Add(EnsureLayer(doc, a));
                break;
            case "set_current_layer":
                logs.Add(SetCurrentLayer(doc, a));
                break;
            case "draw_polyline":
                logs.Add(DrawPolyline(doc, a));
                break;
            case "draw_line":
                logs.Add(DrawLine(doc, a));
                break;
            case "draw_rectangle":
                logs.Add(DrawRectangle(doc, a));
                break;
            case "draw_circle":
                logs.Add(DrawCircle(doc, a));
                break;
            case "create_text":
                logs.Add(CreateText(doc, a));
                break;
            case "create_mtext":
                logs.Add(CreateMText(doc, a));
                break;
            case "update_single_mtext":
                logs.Add(UpdateSingleMText(doc, a));
                break;
            case "read_text":
                logs.AddRange(ReadText(doc, a));
                break;
            case "list_entities":
                logs.AddRange(ListEntities(doc, a));
                break;
            case "insert_block":
                logs.Add(InsertBlock(doc, a));
                break;
            case "create_layout":
                logs.Add(CreateLayout(doc, a));
                break;
            case "copy_layout":
                logs.Add(CopyLayout(doc, a));
                break;
            case "set_current_layout":
                logs.Add(SetCurrentLayout(doc, a));
                break;
            case "generate_lisp":
                logs.Add(GenerateLisp(workspaceRoot, a));
                break;
            case "prompt":
                logs.Add("prompt: " + (a.Value<string>("message") ?? a.Value<string>("text") ?? a.Value<string>("content") ?? "(vide)"));
                break;
            default:
                logs.Add("Type inconnu: " + type);
                break;
        }

        return logs;
    }

    private static string ListLayouts(Document doc)
    {
        try
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                var arr = new JArray();
                foreach (DBDictionaryEntry entry in dict)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    arr.Add(new JObject
                    {
                        ["name"] = layout.LayoutName,
                        ["tabOrder"] = layout.TabOrder,
                        ["isModel"] = layout.ModelType
                    });
                }

                tr.Commit();
                return "list_layouts: " + arr.ToString(Newtonsoft.Json.Formatting.None);
            }
        }
        catch (System.Exception ex)
        {
            return "list_layouts: erreur " + ex.Message;
        }
    }

    private static string CreateLayout(Document doc, JObject a)
    {
        var name = (a.Value<string>("name") ?? a.Value<string>("layout") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            // Accept common aliases: page / number (ex: {"type":"create_layout","page":9})
            var pageTok = a["page"] ?? a["number"] ?? a["index"];
            if (pageTok != null)
            {
                var p = (pageTok.Type == JTokenType.Integer || pageTok.Type == JTokenType.Float)
                    ? pageTok.ToString()
                    : (pageTok.Value<string>() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    name = "Page " + p;
            }
        }
        if (string.IsNullOrWhiteSpace(name))
            return "create_layout: name manquant.";

        try
        {
            using (doc.LockDocument())
            {
                var lm = LayoutManager.Current;
                // If already exists, just switch to it.
                try
                {
                    lm.CurrentLayout = name;
                    return "create_layout: exists -> current_layout=" + name;
                }
                catch
                {
                    // ignore; we'll create
                }

                lm.CreateLayout(name);
                lm.CurrentLayout = name;
                return "create_layout: ok -> current_layout=" + name;
            }
        }
        catch (System.Exception ex)
        {
            return "create_layout: erreur " + ex.Message;
        }
    }

    private static string SetCurrentLayout(Document doc, JObject a)
    {
        var name = (a.Value<string>("name") ?? a.Value<string>("layout") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            var pageTok = a["page"] ?? a["number"] ?? a["index"];
            if (pageTok != null)
            {
                var p = (pageTok.Type == JTokenType.Integer || pageTok.Type == JTokenType.Float)
                    ? pageTok.ToString()
                    : (pageTok.Value<string>() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    name = "Page " + p;
            }
        }
        if (string.IsNullOrWhiteSpace(name))
            return "set_current_layout: name manquant.";

        try
        {
            using (doc.LockDocument())
            {
                LayoutManager.Current.CurrentLayout = name;
                return "set_current_layout: ok -> " + name;
            }
        }
        catch (System.Exception ex)
        {
            return "set_current_layout: erreur " + ex.Message;
        }
    }

    private static string CopyLayout(Document doc, JObject a)
    {
        // Copy from an existing layout template to a new layout name.
        var from = (a.Value<string>("from") ?? a.Value<string>("template") ?? a.Value<string>("source") ?? "").Trim();
        var to = (a.Value<string>("to") ?? a.Value<string>("name") ?? a.Value<string>("layout") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(from))
            return "copy_layout: from manquant.";
        if (string.IsNullOrWhiteSpace(to))
            return "copy_layout: to/name manquant.";

        try
        {
            using (doc.LockDocument())
            {
                var lm = LayoutManager.Current;

                // If destination already exists, just activate it.
                try
                {
                    lm.CurrentLayout = to;
                    return "copy_layout: exists -> current_layout=" + to;
                }
                catch
                {
                    // ignore
                }

                // LayoutManager.CopyLayout exists in many AutoCAD .NET APIs but signatures vary.
                // Use reflection to avoid compile-time binding to a specific overload.
                var t = lm.GetType();
                var mi = t.GetMethod("CopyLayout", new[] { typeof(string), typeof(string) })
                         ?? t.GetMethod("CopyLayout", new[] { typeof(string), typeof(string), typeof(int) });

                if (mi == null)
                    return "copy_layout: API CopyLayout introuvable (version AutoCAD).";

                var args = mi.GetParameters().Length == 2
                    ? new object[] { from, to }
                    : new object[] { from, to, 1 };

                mi.Invoke(lm, args);
                lm.CurrentLayout = to;
                return "copy_layout: ok -> current_layout=" + to;
            }
        }
        catch (System.Exception ex)
        {
            return "copy_layout: erreur " + ex.Message;
        }
    }

    private static async Task<string> WebSearchAsync(JObject a)
    {
        var query = (a.Value<string>("query") ?? a.Value<string>("q") ?? "").Trim();
        var max = a.Value<int?>("max_results") ?? a.Value<int?>("max") ?? 5;
        if (string.IsNullOrWhiteSpace(query))
            return "web_search: query manquante.";

        try
        {
            // ConfigureAwait(false) : le thread document est libéré pendant la requête HTTP,
            // la continuation revient sur le SynchronizationContext via l'await parent.
            var arr = await DuckDuckGoSearch.SearchAsync(query, max).ConfigureAwait(false);
            return "web_search: " + arr.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Exception ex)
        {
            return "web_search: erreur " + ex.Message;
        }
    }

    private static void NormalizeActionObject(JObject a, List<string> logs)
    {
        // Alias pour le champ type : certains modèles utilisent "command" ou "action"
        if (a["command"] != null && a["type"] == null)
        { a["type"] = a["command"]; logs.Add("normalize: command -> type."); }
        if (a["action"] != null && a["type"] == null)
        { a["type"] = a["action"]; logs.Add("normalize: action -> type."); }

        // Common aliases from LLMs
        if (a["layer_name"] != null && a["name"] == null)
            a["name"] = a["layer_name"];

        if (a["layerName"] != null && a["name"] == null)
            a["name"] = a["layerName"];

        // Layout aliases
        var t = (a.Value<string>("type") ?? "");
        if ((t.Equals("create_layout", StringComparison.OrdinalIgnoreCase) || t.Equals("set_current_layout", StringComparison.OrdinalIgnoreCase))
            && a["name"] == null)
        {
            if (a["layout_name"] != null) a["name"] = a["layout_name"];
            else if (a["layoutName"] != null) a["name"] = a["layoutName"];
            else if (a["layout"] != null) a["name"] = a["layout"];
            else if (a["pageName"] != null) a["name"] = a["pageName"];
        }

        // If model tries create_layout "from template", transparently convert to copy_layout.
        if (t.Equals("create_layout", StringComparison.OrdinalIgnoreCase) && (a["from"] != null || a["template"] != null || a["source"] != null))
        {
            if (a["name"] == null && a["to"] != null)
                a["name"] = a["to"];

            a["type"] = "copy_layout";
            logs.Add("normalize: create_layout with from/template -> copy_layout.");
            t = "copy_layout";
        }

        if (t.Equals("copy_layout", StringComparison.OrdinalIgnoreCase))
        {
            if (a["from"] == null)
            {
                if (a["template"] != null) a["from"] = a["template"];
                else if (a["source"] != null) a["from"] = a["source"];
                else if (a["layout"] != null) a["from"] = a["layout"];
                else if (a["layout_name"] != null) a["from"] = a["layout_name"];
            }

            if (a["to"] == null)
            {
                if (a["name"] != null) a["to"] = a["name"];
                else if (a["layoutName"] != null) a["to"] = a["layoutName"];
                else if (a["layout_name"] != null) a["to"] = a["layout_name"];
            }
        }

        if (a["layer"] != null && a["name"] == null && ((a.Value<string>("type") ?? "").Equals("ensure_layer", StringComparison.OrdinalIgnoreCase) || (a.Value<string>("type") ?? "").Equals("set_current_layer", StringComparison.OrdinalIgnoreCase)))
            a["name"] = a["layer"];

        if (a["insertPoint"] != null && a["insert"] == null)
            a["insert"] = a["insertPoint"];

        // Fix common bad formats: stringified JSON arrays/objects in string values
        foreach (var prop in a.Properties().ToList())
        {
            if (prop.Value.Type != JTokenType.String) continue;
            var s = (prop.Value.Value<string>() ?? "").Trim();
            if (s.Length == 0) continue;

            if ((s.StartsWith("[") && s.EndsWith("]")) || (s.StartsWith("{") && s.EndsWith("}")))
            {
                try { prop.Value = JToken.Parse(s); } catch { }
                continue;
            }

            // "radius=5" / "center=[10,5]" inside a string value
            var eq = s.IndexOf('=');
            if (eq > 0 && eq < s.Length - 1)
            {
                var key = s.Substring(0, eq).Trim().Trim('"');
                var val = s.Substring(eq + 1).Trim();
                if (key.Length > 0 && a[key] == null)
                {
                    if ((val.StartsWith("[") && val.EndsWith("]")) || (val.StartsWith("{") && val.EndsWith("}")))
                    {
                        try { a[key] = JToken.Parse(val); }
                        catch { a[key] = val; }
                    }
                    else if (double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        a[key] = d;
                    }
                    else
                    {
                        a[key] = val;
                    }
                    logs.Add($"normalize: extrait {key} depuis '{prop.Name}'.");
                }
            }
        }

        // Make draw_polyline closed when last point equals first
        if (a.Value<string>("type")?.Equals("draw_polyline", StringComparison.OrdinalIgnoreCase) == true
            && (a.Value<bool?>("closed") == null)
            && a["points"] is JArray parr
            && parr.Count >= 3)
        {
            var first = AsPoint2d(parr[0]);
            var last = AsPoint2d(parr[parr.Count - 1]);
            if (first != null && last != null && (first.Value.GetDistanceTo(last.Value) < 1e-9))
                a["closed"] = true;
        }

        // Common malformed shapes from LLMs:
        // - draw_line: points:[[x1,y1],[x2,y2]]  -> from/to
        // - draw_rectangle: points:[[x1,y1],[x2,y2]] -> origin + width/height
        var type = (a.Value<string>("type") ?? "").ToLowerInvariant();
        if (a["points"] is JArray ptsArr && ptsArr.Count >= 2)
        {
            var p0 = AsPoint2d(ptsArr[0]);
            var p1 = AsPoint2d(ptsArr[1]);
            if (p0 != null && p1 != null)
            {
                if (type == "draw_line")
                {
                    if (a["from"] == null) a["from"] = new JObject { ["x"] = p0.Value.X, ["y"] = p0.Value.Y, ["z"] = 0 };
                    if (a["to"] == null) a["to"] = new JObject { ["x"] = p1.Value.X, ["y"] = p1.Value.Y, ["z"] = 0 };
                    logs.Add("normalize: draw_line points -> from/to.");
                }
                else if (type == "draw_rectangle")
                {
                    // interpret as opposite corners
                    var x1 = Math.Min(p0.Value.X, p1.Value.X);
                    var y1 = Math.Min(p0.Value.Y, p1.Value.Y);
                    var x2 = Math.Max(p0.Value.X, p1.Value.X);
                    var y2 = Math.Max(p0.Value.Y, p1.Value.Y);
                    if (a["origin"] == null) a["origin"] = new JObject { ["x"] = x1, ["y"] = y1 };
                    if (a["width"] == null) a["width"] = x2 - x1;
                    if (a["height"] == null) a["height"] = y2 - y1;
                    logs.Add("normalize: draw_rectangle points -> origin/width/height.");
                }
            }
        }
    }

    private static string GetDrawingInfo(Document doc)
    {
        var db = doc.Database;
        var info = new JObject
        {
            ["fileName"] = db.Filename ?? "",
            ["insunits"] = db.Insunits.ToString(),
            ["tilemode"] = AsJsonValue(SafeGetVar("TILEMODE")),
            ["ltscale"] = AsJsonValue(SafeGetVar("LTSCALE")),
            ["dimscale"] = AsJsonValue(SafeGetVar("DIMSCALE")),
        };
        return "get_drawing_info: " + info.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string GetVariable(JObject a)
    {
        var name = (a.Value<string>("name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return "get_variable: name manquant.";
        var val = SafeGetVar(name);
        return "get_variable: " + new JObject { ["name"] = name, ["value"] = AsJsonValue(val) }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string SetVariable(JObject a)
    {
        var name = (a.Value<string>("name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return "set_variable: name manquant.";
        if (!AllowedSetVariables.Contains(name))
            return $"set_variable: variable '{name}' non autorisée (liste blanche).";
        if (!a.TryGetValue("value", out var v)) return "set_variable: value manquant.";
        try
        {
            Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable(name, JTokenToClr(v));
            return "set_variable: OK " + name;
        }
        catch (Exception ex)
        {
            return "set_variable: erreur " + ex.Message;
        }
    }

    private static object? SafeGetVar(string name)
    {
        try { return Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable(name); }
        catch { return null; }
    }

    private static JToken AsJsonValue(object? v)
    {
        if (v == null) return JValue.CreateNull();
        try { return JToken.FromObject(v); }
        catch { return new JValue(v.ToString()); }
    }

    private static string ListLayers(Document doc)
    {
        var arr = new JArray();
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                arr.Add(new JObject
                {
                    ["name"] = ltr.Name,
                    ["isOff"] = ltr.IsOff,
                    ["isFrozen"] = ltr.IsFrozen,
                    ["isLocked"] = ltr.IsLocked,
                    ["colorIndex"] = ltr.Color?.ColorIndex ?? 0
                });
            }
            tr.Commit();
        }
        return "list_layers: " + arr.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ListBlocks(Document doc)
    {
        var arr = new JArray();
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsAnonymous) continue;
                arr.Add(btr.Name);
            }
            tr.Commit();
        }
        return "list_blocks: " + arr.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string SelectEntities(Document doc, JObject a)
    {
        // Simple filter: types (fragments) + layer + colorIndex
        var types = a["types"] as JArray;
        var want = new HashSet<string>((types ?? new JArray()).Select(t => (t.ToString() ?? "").ToLowerInvariant()).Where(x => x.Length > 0));
        var layer = (a.Value<string>("layer") ?? "").Trim();
        var colorIndex = a.Value<int?>("colorIndex");

        var handles = new JArray();
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is not Entity ent) continue;
                var t = obj.GetType().Name.ToLowerInvariant();
                var okType = want.Count == 0 || want.Any(w => t.Contains(w));
                var okLayer = string.IsNullOrWhiteSpace(layer) || string.Equals(ent.Layer, layer, StringComparison.OrdinalIgnoreCase);
                var okColor = colorIndex == null || (ent.Color?.ColorIndex ?? 0) == colorIndex.Value;
                if (okType && okLayer && okColor)
                    handles.Add(ent.Handle.ToString());
            }
            tr.Commit();
        }

        return "select_entities: " + new JObject { ["handles"] = handles }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string GetSelection(Document doc)
    {
        var ed = doc.Editor;
        var res = ed.SelectImplied();
        if (res.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK || res.Value == null)
            return "get_selection: {\"handles\":[]}";

        var arr = new JArray();
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            foreach (var id in res.Value.GetObjectIds())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    arr.Add(ent.Handle.ToString());
            }
            tr.Commit();
        }
        return "get_selection: " + new JObject { ["handles"] = arr }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string GetEntityProperties(Document doc, JObject a)
    {
        // Accept:
        // - {handle:"ABCD"}
        // - {selection:true} -> uses implied selection (first entity)
        // - {handles:[...]} -> returns first handle's properties (MVP)
        ObjectId? id = null;
        var handleStr = a.Value<string>("handle");

        if (!string.IsNullOrWhiteSpace(handleStr))
        {
            id = ResolveObjectId(doc, handleStr);
        }
        else if (a.Value<bool?>("selection") == true)
        {
            var ed = doc.Editor;
            var sel = ed.SelectImplied();
            if (sel.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK && sel.Value != null)
            {
                var ids = sel.Value.GetObjectIds();
                if (ids.Length > 0) id = ids[0];
            }
        }
        else if (a["handles"] is JArray hs && hs.Count > 0)
        {
            id = ResolveObjectId(doc, hs[0]?.ToString());
        }

        if (id == null) return "get_entity_properties: handle invalide.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForRead);
            if (obj is not Entity ent)
                return "get_entity_properties: pas une entité.";

            var o = new JObject
            {
                ["handle"] = ent.Handle.ToString(),
                ["type"] = ent.GetType().Name,
                ["layer"] = ent.Layer,
                ["colorIndex"] = ent.Color?.ColorIndex ?? 0,
                ["linetype"] = ent.Linetype,
                ["lineweight"] = ent.LineWeight.ToString()
            };

            if (TryGetExtents(ent, out var min, out var max))
            {
                o["extents"] = new JObject
                {
                    ["min"] = new JObject { ["x"] = min.X, ["y"] = min.Y, ["z"] = min.Z },
                    ["max"] = new JObject { ["x"] = max.X, ["y"] = max.Y, ["z"] = max.Z }
                };
            }

            tr.Commit();
            return "get_entity_properties: " + o.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    private static string GetBoundingBox(Document doc, JObject a)
    {
        // either handle or handles[]
        var handlesArr = a["handles"] as JArray;
        var handles = handlesArr != null ? handlesArr.Select(x => x.ToString()).ToList() : new List<string>();
        var single = a.Value<string>("handle");
        if (!string.IsNullOrWhiteSpace(single)) handles.Add(single!);
        if (handles.Count == 0) return "get_bounding_box: handle(s) manquant(s).";

        var has = false;
        Point3d min = default, max = default;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            foreach (var h in handles)
            {
                var id = ResolveObjectId(doc, h);
                if (id == null) continue;
                var obj = tr.GetObject(id.Value, OpenMode.ForRead);
                if (obj is not Entity ent) continue;
                if (!TryGetExtents(ent, out var eMin, out var eMax)) continue;

                if (!has)
                {
                    has = true;
                    min = eMin; max = eMax;
                }
                else
                {
                    min = new Point3d(Math.Min(min.X, eMin.X), Math.Min(min.Y, eMin.Y), Math.Min(min.Z, eMin.Z));
                    max = new Point3d(Math.Max(max.X, eMax.X), Math.Max(max.Y, eMax.Y), Math.Max(max.Z, eMax.Z));
                }
            }
            tr.Commit();
        }

        if (!has) return "get_bounding_box: extents introuvables.";
        var outJ = new JObject
        {
            ["min"] = new JObject { ["x"] = min.X, ["y"] = min.Y, ["z"] = min.Z },
            ["max"] = new JObject { ["x"] = max.X, ["y"] = max.Y, ["z"] = max.Z }
        };
        return "get_bounding_box: " + outJ.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string GetDrawingExtents(Document doc)
    {
        var db = doc.Database;
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var has = false;
            Point3d min = default, max = default;
            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is not Entity ent) continue;
                if (!TryGetExtents(ent, out var eMin, out var eMax)) continue;
                if (!has) { has = true; min = eMin; max = eMax; }
                else
                {
                    min = new Point3d(Math.Min(min.X, eMin.X), Math.Min(min.Y, eMin.Y), Math.Min(min.Z, eMin.Z));
                    max = new Point3d(Math.Max(max.X, eMax.X), Math.Max(max.Y, eMax.Y), Math.Max(max.Z, eMax.Z));
                }
            }
            tr.Commit();
            if (!has) return "get_drawing_extents: aucun extents.";
            var outJ = new JObject
            {
                ["min"] = new JObject { ["x"] = min.X, ["y"] = min.Y, ["z"] = min.Z },
                ["max"] = new JObject { ["x"] = max.X, ["y"] = max.Y, ["z"] = max.Z }
            };
            return "get_drawing_extents: " + outJ.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    private static string MoveEntity(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "move_entity: handle invalide.";

        var dx = a.Value<double?>("dx") ?? 0;
        var dy = a.Value<double?>("dy") ?? 0;
        var dz = a.Value<double?>("dz") ?? 0;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not Entity ent) return "move_entity: pas une entité.";
            ent.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, dz)));
            tr.Commit();
        }
        return "move_entity: OK.";
    }

    private static string RotateEntity(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "rotate_entity: handle invalide.";
        var angle = a.Value<double?>("angle") ?? 0;
        var basePt = AsPoint3d(a["base"]) ?? Point3d.Origin;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not Entity ent) return "rotate_entity: pas une entité.";
            ent.TransformBy(Matrix3d.Rotation(angle, Vector3d.ZAxis, basePt));
            tr.Commit();
        }
        return "rotate_entity: OK.";
    }

    private static string ScaleEntity(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "scale_entity: handle invalide.";
        var factor = a.Value<double?>("factor") ?? 1.0;
        var basePt = AsPoint3d(a["base"]) ?? Point3d.Origin;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not Entity ent) return "scale_entity: pas une entité.";
            ent.TransformBy(Matrix3d.Scaling(factor, basePt));
            tr.Commit();
        }
        return "scale_entity: OK.";
    }

    private static string DeleteEntity(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "delete_entity: handle invalide.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not Entity ent) return "delete_entity: pas une entité.";
            ent.Erase();
            tr.Commit();
        }
        return "delete_entity: OK.";
    }

    private static string ChangeEntityProperties(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "change_entity_properties: handle invalide.";

        var layer = a.Value<string>("layer");
        var colorIndex = a.Value<int?>("colorIndex");
        var linetype = a.Value<string>("linetype");

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not Entity ent) return "change_entity_properties: pas une entité.";
            if (!string.IsNullOrWhiteSpace(layer)) ent.Layer = layer;
            if (colorIndex != null) ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)colorIndex.Value);
            if (!string.IsNullOrWhiteSpace(linetype)) ent.Linetype = linetype;
            tr.Commit();
        }
        return "change_entity_properties: OK.";
    }

    private static string ReadBlockAttributes(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "read_block_attributes: handle invalide.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForRead);
            if (obj is not BlockReference br) return "read_block_attributes: pas un BlockReference.";

            var outJ = new JObject { ["handle"] = br.Handle.ToString(), ["attributes"] = new JObject() };
            var attrs = (JObject)outJ["attributes"]!;
            foreach (ObjectId aid in br.AttributeCollection)
            {
                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                    attrs[ar.Tag] = ar.TextString ?? "";
            }
            tr.Commit();
            return "read_block_attributes: " + outJ.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    private static string UpdateBlockAttributes(Document doc, JObject a)
    {
        var id = ResolveObjectId(doc, a.Value<string>("handle"));
        if (id == null) return "update_block_attributes: handle invalide.";
        if (a["attributes"] is not JObject attrs) return "update_block_attributes: attributes manquant.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(id.Value, OpenMode.ForWrite);
            if (obj is not BlockReference br) return "update_block_attributes: pas un BlockReference.";

            foreach (ObjectId aid in br.AttributeCollection)
            {
                if (tr.GetObject(aid, OpenMode.ForWrite) is AttributeReference ar)
                {
                    var val = attrs.Value<string>(ar.Tag);
                    if (val != null) ar.TextString = val;
                }
            }
            tr.Commit();
        }
        return "update_block_attributes: OK.";
    }

    private static string CreateText(Document doc, JObject a)
    {
        var insert = AsPoint3d(a["insert"]) ?? AsPoint3d(a["position"]) ?? AsPoint3d(a["pt"]);
        if (insert == null) return "create_text: insert invalide.";
        var text = a.Value<string>("text") ?? "";
        if (string.IsNullOrWhiteSpace(text)) return "create_text: text vide.";
        var height = a.Value<double?>("height") ?? 2.5;
        var layer = a.Value<string>("layer");

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var dt = new DBText
            {
                Position = insert.Value,
                TextString = text,
                Height = height
            };
            if (!string.IsNullOrWhiteSpace(layer)) dt.Layer = layer;
            btr.AppendEntity(dt);
            tr.AddNewlyCreatedDBObject(dt, true);
            tr.Commit();
        }
        return "create_text: OK.";
    }

    private static ObjectId? ResolveObjectId(Document doc, string? handleStr)
    {
        if (string.IsNullOrWhiteSpace(handleStr)) return null;
        try
        {
            // Handles are typically hex strings.
            var h = long.Parse(handleStr.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var handle = new Handle(h);
            return doc.Database!.GetObjectId(false, handle, 0);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetExtents(Entity ent, out Point3d min, out Point3d max)
    {
        min = default;
        max = default;
        try
        {
            var ex = ent.GeometricExtents;
            min = ex.MinPoint;
            max = ex.MaxPoint;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? JTokenToClr(JToken v)
    {
        return v.Type switch
        {
            JTokenType.Integer => v.Value<long>(),
            JTokenType.Float => v.Value<double>(),
            JTokenType.Boolean => v.Value<bool>(),
            JTokenType.String => v.Value<string>(),
            _ => v.ToString()
        };
    }

    private static string EnsureLayer(Document doc, JObject a)
    {
        var name = (a.Value<string>("name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return "ensure_layer: name manquant.";

        var colorIndex = a.Value<int?>("colorIndex");

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var db = doc.Database;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord { Name = name };
                if (colorIndex != null)
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)colorIndex.Value);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                tr.Commit();
                return $"ensure_layer: créé '{name}'.";
            }

            tr.Commit();
            return $"ensure_layer: existe '{name}'.";
        }
    }

    private static string SetCurrentLayer(Document doc, JObject a)
    {
        var name = (a.Value<string>("name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return "set_current_layer: name manquant.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var db = doc.Database;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
                return $"set_current_layer: layer introuvable '{name}'.";

            db.Clayer = lt[name];
            tr.Commit();
            return $"set_current_layer: OK '{name}'.";
        }
    }

    private static string DrawPolyline(Document doc, JObject a)
    {
        var pts = new List<Point2d>();
        if (a["points"] is JArray parr)
        {
            foreach (var p in parr)
            {
                var pt = AsPoint2d(p);
                if (pt != null) pts.Add(pt.Value);
            }
        }

        if (pts.Count < 2)
            return "draw_polyline: points invalides.";

        var closed = a.Value<bool?>("closed") ?? false;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var pl = new Polyline(pts.Count);
            for (var i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = closed;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
        }

        return $"draw_polyline: OK ({pts.Count} points, closed={closed}).";
    }

    private static string DrawLine(Document doc, JObject a)
    {
        var from = AsPoint3d(a["from"]);
        var to = AsPoint3d(a["to"]);
        if (from == null || to == null)
            return "draw_line: from/to invalide.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var ln = new Line(from.Value, to.Value);
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            tr.Commit();
        }

        return "draw_line: OK.";
    }

    private static string DrawRectangle(Document doc, JObject a)
    {
        // Accept origin or insert (same meaning: lower-left corner)
        var origin = AsPoint2d(a["origin"]) ?? AsPoint2d(a["insert"]);

        // Accept width/height or w/h or size:[w,h]
        var w = a.Value<double?>("width") ?? a.Value<double?>("w");
        var h = a.Value<double?>("height") ?? a.Value<double?>("h");
        if ((w == null || h == null) && a["size"] is JArray sizeArr && sizeArr.Count >= 2)
        {
            w ??= sizeArr[0].Value<double?>();
            h ??= sizeArr[1].Value<double?>();
        }

        if (origin == null || w == null || h == null || w <= 0 || h <= 0)
            return "draw_rectangle: origin(insert)/width(height) invalide.";

        var p0 = origin.Value;
        var pts = new[]
        {
            new Point2d(p0.X, p0.Y),
            new Point2d(p0.X + w.Value, p0.Y),
            new Point2d(p0.X + w.Value, p0.Y + h.Value),
            new Point2d(p0.X, p0.Y + h.Value),
        };

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var pl = new Polyline(4);
            for (var i = 0; i < 4; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = true;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
        }

        return $"draw_rectangle: OK ({w.Value.ToString(CultureInfo.InvariantCulture)}x{h.Value.ToString(CultureInfo.InvariantCulture)}).";
    }

    private static string DrawCircle(Document doc, JObject a)
    {
        var center = AsPoint3d(a["center"]);
        if (center == null)
            return "draw_circle: center invalide.";

        var radius = a.Value<double?>("radius");
        var diameter = a.Value<double?>("diameter");
        var r = radius ?? (diameter != null ? diameter.Value / 2.0 : (double?)null);
        if (r == null || r <= 0)
            return "draw_circle: radius/diameter invalide.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var circ = new Circle(center.Value, Vector3d.ZAxis, r.Value);
            btr.AppendEntity(circ);
            tr.AddNewlyCreatedDBObject(circ, true);
            tr.Commit();
        }

        return $"draw_circle: OK (r={r.Value.ToString(CultureInfo.InvariantCulture)}).";
    }

    private static string CreateMText(Document doc, JObject a)
    {
        var insert = AsPoint3d(a["insert"]);
        if (insert == null)
            return "create_mtext: insert invalide.";

        var text = a.Value<string>("text") ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return "create_mtext: text vide.";

        var height = a.Value<double?>("height") ?? 2.5;
        var width = a.Value<double?>("width");
        var layer = a.Value<string>("layer");

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var mt = new MText
            {
                Location = insert.Value,
                TextHeight = height,
                Contents = text
            };
            if (width != null && width > 0) mt.Width = width.Value;
            if (!string.IsNullOrWhiteSpace(layer)) mt.Layer = layer;
            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
            tr.Commit();
        }

        return "create_mtext: OK.";
    }

    private static string UpdateSingleMText(Document doc, JObject a)
    {
        var newText = a.Value<string>("text") ?? "";
        var mode = (a.Value<string>("mode") ?? "replace").ToLowerInvariant(); // replace|append

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
            ObjectId? only = null;
            var count = 0;

            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is MText)
                {
                    count++;
                    if (count > 1) break;
                    only = id;
                }
            }

            if (count == 0 || only == null)
                return "update_single_mtext: aucun MTEXT trouvé.";
            if (count > 1)
                return "update_single_mtext: plusieurs MTEXT trouvés (pas unique).";

            var mt = (MText)tr.GetObject(only.Value, OpenMode.ForWrite);
            if (mode == "append")
                mt.Contents = (mt.Contents ?? "") + newText;
            else
                mt.Contents = newText;

            tr.Commit();
            return "update_single_mtext: OK.";
        }
    }

    private static IEnumerable<string> ReadText(Document doc, JObject a)
    {
        // scope: "single" (default) or "all"
        var scope = (a.Value<string>("scope") ?? "single").ToLowerInvariant();
        var logs = new List<string>();

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
            var items = new List<string>();

            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is MText mt)
                {
                    items.Add(mt.Contents ?? "");
                }
                else if (obj is DBText dt)
                {
                    items.Add(dt.TextString ?? "");
                }
            }

            tr.Commit();

            if (items.Count == 0)
            {
                logs.Add("read_text: aucun MTEXT/TEXT trouvé.");
                return logs;
            }

            if (scope == "all")
            {
                logs.Add($"read_text: {items.Count} texte(s).");
                for (var i = 0; i < items.Count; i++)
                    logs.Add($"read_text[{i + 1}]: {items[i]}");
                return logs;
            }

            // single
            if (items.Count == 1)
            {
                logs.Add("read_text: " + items[0]);
                return logs;
            }

            logs.Add($"read_text: {items.Count} textes trouvés (pas unique). Utilise scope='all' ou précise un filtre (à venir).");
            return logs;
        }
    }

    private static IEnumerable<string> ListEntities(Document doc, JObject a)
    {
        var logs = new List<string>();
        var types = a["types"] as JArray;
        var want = new HashSet<string>((types ?? new JArray()).Select(t => (t.ToString() ?? "").ToLowerInvariant()).Where(x => x.Length > 0));
        var layer = (a.Value<string>("layer") ?? "").Trim();

        int total = 0, match = 0;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                total++;
                var obj = tr.GetObject(id, OpenMode.ForRead);
                var t = obj.GetType().Name.ToLowerInvariant();
                var okType = want.Count == 0 || want.Any(w => t.Contains(w));
                var okLayer = string.IsNullOrWhiteSpace(layer) || (obj is Entity e && string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase));
                if (okType && okLayer) match++;
            }
            tr.Commit();
        }

        logs.Add($"list_entities: total={total}, match={match}.");
        return logs;
    }

    private static string InsertBlock(Document doc, JObject a)
    {
        var name = (a.Value<string>("name") ?? "").Trim();
        var insert = AsPoint3d(a["insert"]);
        if (string.IsNullOrWhiteSpace(name) || insert == null)
            return "insert_block: name/insert invalide.";

        var scale = a.Value<double?>("scale") ?? 1.0;
        var rotation = a.Value<double?>("rotation") ?? 0.0;

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var db = doc.Database;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(name))
                return $"insert_block: bloc introuvable '{name}'.";

            var btrSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(insert.Value, bt[name])
            {
                ScaleFactors = new Scale3d(scale),
                Rotation = rotation
            };
            btrSpace.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            // Attributes support (optional): attributes: { "TAG":"value", ... }
            if (a["attributes"] is JObject attrs)
            {
                var btrDef = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                foreach (ObjectId id in btrDef)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
                    {
                        var tag = (ad.Tag ?? "").Trim();
                        if (tag.Length == 0) continue;
                        var val = attrs.Value<string>(tag);
                        if (val == null) continue;

                        var ar = new AttributeReference();
                        ar.SetAttributeFromBlock(ad, br.BlockTransform);
                        ar.TextString = val;
                        br.AttributeCollection.AppendAttribute(ar);
                        tr.AddNewlyCreatedDBObject(ar, true);
                    }
                }
            }

            tr.Commit();
        }

        return $"insert_block: OK '{name}'.";
    }

    private static string GenerateLisp(string workspaceRoot, JObject a)
    {
        var name = Slug(a.Value<string>("name") ?? "generated");
        var code = a.Value<string>("code") ?? "";
        if (string.IsNullOrWhiteSpace(code))
            return "generate_lisp: code vide.";

        var lispDir = Path.Combine(workspaceRoot, "generated", "lisp");
        Directory.CreateDirectory(lispDir);

        var file = Path.Combine(lispDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{name}.lsp");
        File.WriteAllText(file, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return "generate_lisp: écrit " + file;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ' || ch == '_' || ch == '-') sb.Append('-');
        }
        var outS = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(outS) ? "generated" : outS;
    }

    private static Point2d? AsPoint2d(JToken? o)
    {
        if (o == null) return null;
        if (o.Type == JTokenType.Object)
        {
            var x = o.Value<double?>("x");
            var y = o.Value<double?>("y");
            if (x != null && y != null) return new Point2d(x.Value, y.Value);
        }
        if (o.Type == JTokenType.Array)
        {
            var arr = (JArray)o;
            if (arr.Count >= 2)
            {
                var x = arr[0].Value<double?>();
                var y = arr[1].Value<double?>();
                if (x != null && y != null) return new Point2d(x.Value, y.Value);
            }
        }
        return null;
    }

    private static Point3d? AsPoint3d(JToken? o)
    {
        if (o == null) return null;
        if (o.Type == JTokenType.String)
        {
            var s = (o.Value<string>() ?? "").Trim();
            // Accept "x,y" or "x y" or "x;y"
            s = s.Replace(';', ',');
            var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                var z = 0.0;
                if (parts.Length >= 3 && double.TryParse(parts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var zz))
                    z = zz;
                return new Point3d(x, y, z);
            }
        }
        if (o.Type == JTokenType.Object)
        {
            var x = o.Value<double?>("x");
            var y = o.Value<double?>("y");
            var z = o.Value<double?>("z") ?? 0;
            if (x != null && y != null) return new Point3d(x.Value, y.Value, z);
        }
        if (o.Type == JTokenType.Array)
        {
            var arr = (JArray)o;
            if (arr.Count >= 2)
            {
                var x = arr[0].Value<double?>();
                var y = arr[1].Value<double?>();
                var z = arr.Count >= 3 ? (arr[2].Value<double?>() ?? 0) : 0;
                if (x != null && y != null) return new Point3d(x.Value, y.Value, z);
            }
        }
        return null;
    }
}

