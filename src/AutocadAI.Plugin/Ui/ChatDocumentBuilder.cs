using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace AutocadAI.Ui;

public static class ChatDocumentBuilder
{
    private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex AutocadPrefixRegex = new Regex(@"^\s*AutoCAD\s*:\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FlowDocument Build(string transcript)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235))
        };

        var lines = (transcript ?? "").Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw ?? "";
            if (string.IsNullOrWhiteSpace(line))
            {
                doc.Blocks.Add(new Paragraph(new Run(" ")) { Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }

            // Speaker styling
            if (line.StartsWith("Vous:", StringComparison.OrdinalIgnoreCase))
            {
                doc.Blocks.Add(MakeSpeakerParagraph("Vous", line.Substring(5).Trim(), Color.FromRgb(148, 163, 184)));
                continue;
            }
            if (line.StartsWith("IA:", StringComparison.OrdinalIgnoreCase))
            {
                doc.Blocks.Add(MakeSpeakerParagraph("IA", line.Substring(3).Trim(), Color.FromRgb(226, 232, 240)));
                continue;
            }
            // Accept "AutoCAD:" and "AutoCAD  :" variants (some hosts insert extra spaces)
            if (AutocadPrefixRegex.IsMatch(line))
            {
                var content = AutocadPrefixRegex.Replace(line, "");

                // Try render structured tables for known results
                if (TryAddAutocadTableBlocks(doc, content))
                    continue;

                doc.Blocks.Add(MakeSpeakerParagraph("AutoCAD", content, Color.FromRgb(167, 139, 250)));
                continue;
            }

            // Headings (simple)
            if (line.StartsWith("## "))
            {
                var p = new Paragraph(new Run(line.Substring(3).Trim()))
                {
                    Margin = new Thickness(0, 10, 0, 6),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold
                };
                doc.Blocks.Add(p);
                continue;
            }

            // Numbered lists: "1. item"
            if (IsNumberedItem(line, out var num, out var itemText))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Decimal, Margin = new Thickness(18, 2, 0, 6) };
                list.ListItems.Add(new ListItem(MakeInlineParagraph(itemText)));
                doc.Blocks.Add(list);
                continue;
            }

            // Bullets "- item"
            if (line.TrimStart().StartsWith("- "))
            {
                var text = line.TrimStart().Substring(2).Trim();
                var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(18, 2, 0, 6) };
                list.ListItems.Add(new ListItem(MakeInlineParagraph(text)));
                doc.Blocks.Add(list);
                continue;
            }

            doc.Blocks.Add(MakeInlineParagraph(line));
        }

        return doc;
    }

    private static bool TryAddAutocadTableBlocks(FlowDocument doc, string content)
    {
        // Expected lines:
        // - "list_layers: [ ... ]"
        // - "list_blocks: [ ... ]"
        // - "web_search: [ ... ]"
        var c = content ?? "";

        if (TryParseJsonPayload(c, "list_layers:", out var layersTok) && layersTok is JArray layersArr)
        {
            doc.Blocks.Add(MakeSpeakerParagraph("AutoCAD", "list_layers", Color.FromRgb(167, 139, 250)));
            doc.Blocks.Add(MakeLayersTable(layersArr));
            return true;
        }

        if (TryParseJsonPayload(c, "list_blocks:", out var blocksTok) && blocksTok is JArray blocksArr)
        {
            doc.Blocks.Add(MakeSpeakerParagraph("AutoCAD", "list_blocks", Color.FromRgb(167, 139, 250)));
            doc.Blocks.Add(MakeSimpleListTable(blocksArr, header: "name"));
            return true;
        }

        if (TryParseJsonPayload(c, "web_search:", out var wsTok) && wsTok is JArray wsArr)
        {
            doc.Blocks.Add(MakeSpeakerParagraph("AutoCAD", "web_search", Color.FromRgb(167, 139, 250)));
            doc.Blocks.Add(MakeWebSearchTable(wsArr));
            return true;
        }

        // Generic fallback: "<label>: [ ... ]" or "<label>: { ... }"
        // Useful for any new primitives that return JSON arrays/objects.
        var idx = c.IndexOf(':');
        if (idx > 0 && idx < c.Length - 1)
        {
            var label = c.Substring(0, idx).Trim();
            var json = c.Substring(idx + 1).Trim();
            if (json.StartsWith("[") || json.StartsWith("{"))
            {
                try
                {
                    var tok = JToken.Parse(json);
                    if (tok is JArray anyArr)
                    {
                        doc.Blocks.Add(MakeSpeakerParagraph("AutoCAD", label, Color.FromRgb(167, 139, 250)));
                        doc.Blocks.Add(MakeGenericArrayTable(anyArr));
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        return false;
    }

    private static bool TryParseJsonPayload(string content, string prefix, out JToken token)
    {
        token = JValue.CreateNull();
        if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var json = content.Substring(prefix.Length).Trim();
        if (json.Length == 0) return false;
        try
        {
            token = JToken.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Block MakeLayersTable(JArray arr)
    {
        var table = NewTable(new[] { "name", "color", "off", "frozen", "locked" });
        var max = Math.Min(arr.Count, 50);

        for (var i = 0; i < max; i++)
        {
            if (arr[i] is not JObject o) continue;
            AddRow(table,
                o.Value<string>("name") ?? "",
                (o["colorIndex"]?.ToString() ?? ""),
                (o["isOff"]?.ToString() ?? ""),
                (o["isFrozen"]?.ToString() ?? ""),
                (o["isLocked"]?.ToString() ?? "")
            );
        }

        if (arr.Count > max)
        {
            var p = new Paragraph(new Run($"… {arr.Count - max} autres calques (affichage limité à {max})."))
            {
                Margin = new Thickness(0, 4, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175))
            };
            var section = new Section();
            section.Blocks.Add(table);
            section.Blocks.Add(p);
            return section;
        }

        return table;
    }

    private static Block MakeWebSearchTable(JArray arr)
    {
        var table = NewTable(new[] { "title", "url", "snippet" });
        var max = Math.Min(arr.Count, 8);
        for (var i = 0; i < max; i++)
        {
            if (arr[i] is not JObject o) continue;
            AddRow(table,
                o.Value<string>("title") ?? "",
                o.Value<string>("url") ?? "",
                o.Value<string>("snippet") ?? ""
            );
        }
        return table;
    }

    private static Block MakeSimpleListTable(JArray arr, string header)
    {
        var table = NewTable(new[] { header });
        var max = Math.Min(arr.Count, 50);
        for (var i = 0; i < max; i++)
            AddRow(table, arr[i]?.ToString() ?? "");
        return table;
    }

    private static Block MakeGenericArrayTable(JArray arr)
    {
        // If it's an array of objects, union keys into columns. Otherwise show single column.
        var max = Math.Min(arr.Count, 50);
        var objs = arr.Take(max).OfType<JObject>().ToList();
        if (objs.Count == 0)
            return MakeSimpleListTable(arr, header: "value");

        var keys = objs
            .SelectMany(o => o.Properties().Select(p => p.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10) // avoid ultra-wide tables
            .ToArray();

        var table = NewTable(keys.Length == 0 ? new[] { "value" } : keys);
        foreach (var o in objs)
        {
            if (keys.Length == 0)
            {
                AddRow(table, o.ToString(Newtonsoft.Json.Formatting.None));
                continue;
            }

            var cells = keys.Select(k => o[k]?.ToString() ?? "").ToArray();
            AddRow(table, cells);
        }

        if (arr.Count > max)
        {
            var p = new Paragraph(new Run($"… {arr.Count - max} autres éléments (affichage limité à {max})."))
            {
                Margin = new Thickness(0, 4, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175))
            };
            var section = new Section();
            section.Blocks.Add(table);
            section.Blocks.Add(p);
            return section;
        }

        return table;
    }

    private static Table NewTable(string[] headers)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 10)
        };

        foreach (var _ in headers)
            table.Columns.Add(new TableColumn { Width = GridLength.Auto });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var headerRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)) };
        foreach (var h in headers)
        {
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run(h))
            {
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
            })
            { BorderBrush = new SolidColorBrush(Color.FromRgb(38, 50, 68)), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        group.Rows.Add(headerRow);

        return table;
    }

    private static void AddRow(Table table, params string[] cells)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Length; i++)
        {
            var text = cells[i] ?? "";
            var cell = new TableCell(new Paragraph(new Run(text))
            {
                Margin = new Thickness(6, 3, 6, 3),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235))
            })
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 50, 68)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            row.Cells.Add(cell);
        }
        table.RowGroups[0].Rows.Add(row);
    }

    private static Paragraph MakeSpeakerParagraph(string label, string content, Color labelColor)
    {
        var p = new Paragraph { Margin = new Thickness(0, 6, 0, 2) };
        p.Inlines.Add(new Run(label + "  ") { Foreground = new SolidColorBrush(labelColor), FontWeight = FontWeights.SemiBold });
        foreach (var inline in ParseBold(content))
            p.Inlines.Add(inline);
        return p;
    }

    private static Paragraph MakeInlineParagraph(string text)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var inline in ParseBold(text))
            p.Inlines.Add(inline);
        return p;
    }

    private static Inline[] ParseBold(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new Inline[] { new Run("") };

        var parts = BoldRegex.Split(text);
        // Split returns: [normal, bold, normal, bold, ...]
        return parts
            .Select((s, i) =>
                i % 2 == 1
                    ? (Inline)new Run(s) { FontWeight = FontWeights.SemiBold }
                    : new Run(s))
            .ToArray();
    }

    private static bool IsNumberedItem(string line, out int num, out string itemText)
    {
        num = 0;
        itemText = "";
        var t = line.TrimStart();
        var dot = t.IndexOf('.');
        if (dot <= 0 || dot > 3) return false;
        var left = t.Substring(0, dot);
        if (!int.TryParse(left, out num)) return false;
        itemText = t.Substring(dot + 1).Trim();
        return itemText.Length > 0;
    }
}

