using System.Text.Json;

namespace CMAClothingFix;

public sealed class ClothingRule
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string[] RequiredItems { get; set; } = Array.Empty<string>();
    public string TargetItem { get; set; } = ""; // @body means a renderer not owned by clothing.
    public string RendererPath { get; set; } = "";
    public string MeshName { get; set; } = "";
    public int VertexCount { get; set; }
    public bool HideWholeRenderer { get; set; }
    public int[] Submeshes { get; set; } = Array.Empty<int>();
}

internal sealed class RuleStore
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal ClothingRule[] Rules { get; private set; } = Array.Empty<ClothingRule>();
    internal bool Valid { get; private set; }

    internal void Reload()
    {
        try
        {
            if (!File.Exists(Plugin.RulesPath)) File.WriteAllText(Plugin.RulesPath, "[]\n");
            var parsed = JsonSerializer.Deserialize<ClothingRule[]>(File.ReadAllText(Plugin.RulesPath), Json)
                ?? throw new InvalidDataException("Rules must be a JSON array.");
            foreach (var rule in parsed)
            {
                if (rule == null) throw new InvalidDataException("Null rule.");
                if (!rule.Enabled) continue;
                if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.TargetItem) ||
                    string.IsNullOrWhiteSpace(rule.RendererPath) || string.IsNullOrWhiteSpace(rule.MeshName) ||
                    rule.VertexCount <= 0 || rule.RequiredItems == null || rule.RequiredItems.Length == 0 ||
                    rule.RequiredItems.Any(string.IsNullOrWhiteSpace) || rule.Submeshes == null || rule.Submeshes.Any(x => x < 0))
                    throw new InvalidDataException($"Invalid rule '{rule.Name}': exact item/path/mesh identifiers and a positive vertex count are required.");
                if (rule.TargetItem == "@body" && rule.RequiredItems.Contains("@body"))
                    throw new InvalidDataException("@body cannot be an outer garment condition.");
                if (rule.RequiredItems.All(x => x == rule.TargetItem))
                    throw new InvalidDataException($"Rule '{rule.Name}' must require a different outer garment.");
                if ((!rule.HideWholeRenderer && rule.Submeshes.Length == 0) || (rule.HideWholeRenderer && rule.Submeshes.Length > 0))
                    throw new InvalidDataException($"Rule '{rule.Name}' must explicitly choose whole-renderer hiding or nonempty submesh indices, not both.");
            }
            Rules = parsed.Where(x => x.Enabled).ToArray();
            Valid = true;
            Plugin.Logger.LogInfo($"Loaded {Rules.Length} explicit clipping rule(s)." + (Rules.Length == 0 ? " ExperimentalCoverage controls the built-in garment masks." : " Explicit rules override experimental coverage."));
        }
        catch (Exception ex)
        {
            // An invalid file must never leave stale masking active.
            Rules = Array.Empty<ClothingRule>();
            Valid = false;
            Plugin.Logger.LogWarning("Rules disabled: " + ex.Message);
        }
    }
}
