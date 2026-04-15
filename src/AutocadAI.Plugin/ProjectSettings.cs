using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AutocadAI;

[DataContract]
public sealed class ProjectSettings
{
    [DataMember(Order = 1)]
    public string PolicyLevel { get; set; } = "local-only"; // local-only|minimal-cloud|sanitized-cloud

    [DataMember(Order = 2)]
    public string PreferredEngine { get; set; } = "auto"; // auto|local|cloud

    [DataMember(Order = 3)]
    public string LocalEndpoint { get; set; } = "http://localhost:1234/v1/chat/completions"; // LM Studio (OpenAI-like)

    [DataMember(Order = 4)]
    public string LocalModel { get; set; } = "local-model"; // LM Studio model id (ex: "qwen2.5-coder-7b-instruct")

    [DataMember(Order = 5)]
    public string CloudProvider { get; set; } = "anthropic";

    [DataMember(Order = 6)]
    public bool EnableAiLogs { get; set; } = true;

    [DataMember(Order = 7)]
    public bool EnableWebSearch { get; set; } = true;

    [DataMember(Order = 8)]
    public string InteractionMode { get; set; } = "autocad"; // autocad|general

    public static ProjectSettings LoadOrCreate(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new ProjectSettings();

            using var fs = File.OpenRead(path);
            var ser = new DataContractJsonSerializer(typeof(ProjectSettings));
            var obj = ser.ReadObject(fs) as ProjectSettings;
            return obj ?? new ProjectSettings();
        }
        catch
        {
            return new ProjectSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var fs = File.Create(path);
        var ser = new DataContractJsonSerializer(typeof(ProjectSettings));
        ser.WriteObject(fs, this);
    }

    public string ToPrettyJson()
    {
        using var ms = new MemoryStream();
        var ser = new DataContractJsonSerializer(typeof(ProjectSettings));
        ser.WriteObject(ms, this);
        var raw = Encoding.UTF8.GetString(ms.ToArray());
        // Reformater avec Newtonsoft.Json pour une sortie lisible (#10)
        return JToken.Parse(raw).ToString(Newtonsoft.Json.Formatting.Indented);
    }
}

