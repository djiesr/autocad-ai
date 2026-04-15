using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AutocadAI.Web;

public static class DuckDuckGoSearch
{
    private static readonly HttpClient Http = new HttpClient();

    // Very small HTML parser for DuckDuckGo HTML endpoint.
    private static readonly Regex ResultLink = new Regex(
        "<a[^>]*class=\"result__a\"[^>]*href=\"(?<url>[^\"]+)\"[^>]*>(?<title>[\\s\\S]*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Snippet = new Regex(
        "<a[^>]*class=\"result__snippet\"[^>]*>(?<snippet>[\\s\\S]*?)</a>|<div[^>]*class=\"result__snippet\"[^>]*>(?<snippet2>[\\s\\S]*?)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StripTags = new Regex("<[^>]+>", RegexOptions.Compiled);

    public static async Task<JArray> SearchAsync(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new JArray();
        maxResults = Math.Max(1, Math.Min(10, maxResults));

        // DuckDuckGo HTML endpoint
        var url = "https://duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "AutoCAD-AI/1.0 (+local plugin)");

        var html = await (await Http.SendAsync(req).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);

        var results = new JArray();
        var linkMatches = ResultLink.Matches(html);
        var snippetMatches = Snippet.Matches(html);

        for (var i = 0; i < linkMatches.Count && results.Count < maxResults; i++)
        {
            var m = linkMatches[i];
            var rawUrl = WebUtility.HtmlDecode(m.Groups["url"].Value);
            var titleHtml = m.Groups["title"].Value;
            var title = CleanText(titleHtml);

            string snippet = "";
            if (i < snippetMatches.Count)
            {
                var sm = snippetMatches[i];
                var s = sm.Groups["snippet"].Success ? sm.Groups["snippet"].Value : sm.Groups["snippet2"].Value;
                snippet = CleanText(s);
            }

            results.Add(new JObject
            {
                ["title"] = title,
                ["url"] = rawUrl,
                ["snippet"] = snippet
            });
        }

        return results;
    }

    private static string CleanText(string html)
    {
        var t = WebUtility.HtmlDecode(StripTags.Replace(html ?? "", " "));
        t = Regex.Replace(t, "\\s+", " ").Trim();
        return t;
    }
}

