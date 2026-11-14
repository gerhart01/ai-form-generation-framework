using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AI_FGF;

/// <summary>
/// The JSON Schema of a <c>tool.json</c> (<see cref="ToolSpec"/>), generated from the spec types and their
/// XML documentation, so it cannot drift from what the parser reads. With
/// <c>"$schema": "…/tool.schema.json"</c> in a spec, editors (VS Code, Visual Studio, Rider) complete the
/// property names, show each one's documentation and flag typos and wrong kinds while it is written.
/// <c>AI_FGF.Check --schema</c> writes it; the checked-in copy is schemas/tool.schema.json.
/// </summary>
public static class ToolSpecSchema
{
    public const string Id = "https://ai-fgf.local/schemas/tool.schema.json";

    /// <summary>The schema as indented JSON. <paramref name="xmlDocPath"/>: AI_FGF.Core.xml (default: beside the assembly).</summary>
    public static string Generate(string? xmlDocPath = null)
    {
        Dictionary<string, string> docs = LoadDocs(xmlDocPath ?? Path.ChangeExtension(typeof(ToolSpec).Assembly.Location, ".xml"));
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (ctx, node) =>
            {
                if (ctx.PropertyInfo?.AttributeProvider is MemberInfo { DeclaringType: { } owner } member)
                {
                    // the free-form scalar "default": a string, a number or true/false
                    if (member.Name == nameof(FieldSpec.Default))
                        node = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean", "null") };
                    if (docs.TryGetValue($"P:{owner.FullName}.{member.Name}", out string? text) && node is JsonObject withDoc)
                        withDoc["description"] = text;
                }
                if (node is JsonObject obj && obj.ContainsKey("properties"))
                {
                    if (docs.TryGetValue($"T:{ctx.TypeInfo.Type.FullName}", out string? typeDoc)) obj.TryAdd("description", typeDoc);
                    obj["additionalProperties"] = false;   // a misspelt property is flagged, not silently ignored
                    if (ctx.TypeInfo.Type == typeof(FieldSpec)) obj["required"] = new JsonArray("key", "label");
                }
                return node;
            },
        };
        var schema = (JsonObject)ToolSpec.JsonOptions.GetJsonSchemaAsNode(typeof(ToolSpec), exporter);
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = Id,
            ["title"] = "AI_FGF tool spec (tool.json)",
        };
        foreach (var (key, value) in schema.ToList()) { schema.Remove(key); root[key] = value; }
        if (root["properties"] is JsonObject props)
            props.Insert(0, "$schema", new JsonObject { ["type"] = "string", ["description"] = "This schema, for editor completion and checking." });
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>member id ("P:AI_FGF.FieldSpec.Key") → summary text, from the compiler's XML documentation.</summary>
    private static Dictionary<string, string> LoadDocs(string path)
    {
        var docs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return docs;
        foreach (XElement member in XDocument.Load(path).Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { } name || member.Element("summary") is not { } summary) continue;
            string text = string.Concat(summary.Nodes().Select(n => n switch
            {
                XElement { Name.LocalName: "see" or "seealso" } e => (e.Attribute("cref")?.Value ?? e.Attribute("langword")?.Value ?? "").Split('.', ':').Last(),
                XElement e => e.Value,
                _ => n.ToString(),
            }));
            docs[name] = Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        }
        return docs;
    }
}
