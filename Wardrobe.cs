using System.Text.Json;
using Assets;
using Assets._ReusableScripts.CuchiCuchi;
using Assets._ReusableScripts.CuchiCuchi.Chars.Ropa.Memorias;
using Assets._ReusableScripts.CuchiCuchi.Ropa;
using UnityEngine;
using Assets.TValle.Tools.Runtime.Moddding.Clothing.Maps;

namespace CMAClothingFix;

internal sealed class WornItem
{
    internal string Id, Coverage;
    internal bool Visible;
    internal int Layer = -1;
    internal RopaCubre Flags;
    internal ClothingOrder.Kind Kind;
    internal readonly HashSet<int> Renderers = new();
}

internal sealed class Outfit
{
    internal Character Character;
    internal readonly Dictionary<string, WornItem> Items = new(StringComparer.Ordinal);
    internal readonly List<SkinnedMeshRenderer> Renderers = new();

    internal string OwnerOf(SkinnedMeshRenderer renderer)
    {
        var owners = Items.Values.Where(x => x.Renderers.Contains(renderer.GetInstanceID())).Select(x => x.Id).ToArray();
        return owners.Length == 0 ? "@body" : owners.Length == 1 ? owners[0] : "@ambiguous";
    }

    internal string Signature() => string.Join("|", Items.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value.Visible + ":" + x.Value.Layer + ":" + (int)x.Value.Flags + ":" + x.Value.Kind))
        + ":" + string.Join(",", Renderers.Select(x => x.GetInstanceID()));
}

internal static class Wardrobe
{
    private static readonly HashSet<string> readWarnings = new();
    private static readonly Dictionary<string, int> categories = new(StringComparer.Ordinal);
    private static float nextCategoryScan;
    internal static List<Outfit> Capture()
    {
        if (Time.unscaledTime >= nextCategoryScan)
        {
            categories.Clear();
            foreach (var map in Resources.FindObjectsOfTypeAll<ClothingItemMap>())
                if (map != null && !string.IsNullOrEmpty(map.id)) categories[map.id] = (int)map.type;
            nextCategoryScan = Time.unscaledTime + 2;
        }
        var result = new List<Outfit>();
        foreach (var character in Resources.FindObjectsOfTypeAll<Character>())
        {
            if (character == null || !character.gameObject.scene.IsValid() || !character.gameObject.activeInHierarchy || character.sexo == Sexo.masculino) continue;
            try
            {
                var admin = character.GetComponentInChildren<RopaAdminDeCharacter>(true);
                var manager = admin?.manager;
                if (manager == null || manager.isLoadingConjunto) continue;
                var outfit = new Outfit { Character = character };
                var ids = new Il2CppSystem.Collections.Generic.List<string>();
                manager.ObtenerPiezasIDs(ids.Cast<Il2CppSystem.Collections.Generic.ICollection<string>>(), false);
                var known = new Dictionary<int, SkinnedMeshRenderer>();
                foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer != null) known[renderer.GetInstanceID()] = renderer;
                var pieces = manager.piezasPuestasPorId;
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    if (string.IsNullOrEmpty(id) || pieces == null || !pieces.TryGetValue(id, out var piece) || piece == null) continue;
                    var flags = manager.PiezaCubreFlags(id, false);
                    var item = new WornItem { Id = id, Coverage = flags.ToString(), Flags = flags,
                        Layer = piece.dataDeRopa != null ? (int)piece.dataDeRopa.layer : -1, Visible = !piece.hidden };
                    item.Kind = ClothingOrder.Classify(categories.TryGetValue(id, out int category) ? category : 0, (int)flags);
                    void Add(SkinnedMeshRenderer renderer)
                    {
                        if (renderer == null) return;
                        int key = renderer.GetInstanceID();
                        item.Renderers.Add(key);
                        known[key] = renderer;
                    }
                    Add(piece.skinnedMeshRenderer);
                    if (piece.m_subSkins != null)
                        for (int s = 0; s < piece.m_subSkins.Count; s++)
                            if (piece.m_subSkins[s] != null) Add(piece.m_subSkins[s].skinnedMeshRenderer);
                    item.Visible &= item.Renderers.Any(key => known[key].enabled && known[key].gameObject.activeInHierarchy);
                    outfit.Items[id] = item;
                }
                outfit.Renderers.AddRange(known.Values);
                result.Add(outfit);
            }
            catch (Exception ex)
            {
                string message = $"Wardrobe read failed for '{character.name}': {ex.Message}";
                if (readWarnings.Add(message)) Plugin.Logger.LogWarning(message);
            }
        }
        return result;
    }

    internal static string RendererPath(Transform root, Transform target)
    {
        var parts = new List<string>();
        var current = target;
        while (current != null && current != root) { parts.Add(current.name); current = current.parent; }
        parts.Reverse();
        return (current == null ? "@external/" : "") + string.Join("/", parts);
    }

    internal static void WriteReport(Outfit outfit, VisibilityLedger ledger, CoverageFitter coverage)
    {
        Directory.CreateDirectory(Plugin.ReportsPath);
        var renderers = outfit.Renderers.Select(r =>
        {
            var mesh = coverage.OriginalMesh(r);
            if (mesh == r.sharedMesh) mesh = ledger.OriginalMesh(r);
            var materials = r.sharedMaterials;
            return new
            {
                Owner = outfit.OwnerOf(r),
                RendererPath = RendererPath(outfit.Character.transform, r.transform),
                Enabled = r.enabled, Active = r.gameObject.activeInHierarchy, ForceRenderingOff = r.forceRenderingOff,
                MeshName = mesh != null ? mesh.name : "", VertexCount = mesh != null ? mesh.vertexCount : 0,
                Readable = mesh != null && mesh.isReadable,
                Submeshes = mesh == null ? Array.Empty<object>() : Enumerable.Range(0, mesh.subMeshCount).Select(i => (object)new
                {
                    Index = i, IndexCount = mesh.GetIndexCount(i),
                    BoundsCenter = mesh.GetSubMesh(i).bounds.center.ToString("F4"),
                    BoundsSize = mesh.GetSubMesh(i).bounds.size.ToString("F4"),
                    Material = i < materials.Length && materials[i] != null ? materials[i].name : "",
                    SurfaceType = i < materials.Length && materials[i] != null && materials[i].HasProperty("_SurfaceType") ? (float?)materials[i].GetFloat("_SurfaceType") : null,
                    AlphaCutoff = i < materials.Length && materials[i] != null && materials[i].HasProperty("_AlphaCutoffEnable") ? (float?)materials[i].GetFloat("_AlphaCutoffEnable") : null,
                    ColorAlpha = i < materials.Length && materials[i] != null && materials[i].HasProperty("_BaseColor") ? (float?)materials[i].GetColor("_BaseColor").a : null,
                    ColorTexture = i < materials.Length && materials[i] != null && materials[i].HasProperty("_BaseColorMap") && materials[i].GetTexture("_BaseColorMap") != null,
                    Shader = i < materials.Length && materials[i] != null && materials[i].shader != null ? materials[i].shader.name : ""
                }).ToArray(),
                Bones = r.bones.Select(b => b != null ? b.name : "").ToArray(),
                Shapes = mesh == null ? Array.Empty<object>() : Enumerable.Range(0, mesh.blendShapeCount)
                    .Select(i => (object)new { Name = mesh.GetBlendShapeName(i), Weight = r.GetBlendShapeWeight(i) }).ToArray()
            };
        }).ToArray();
        var report = new
        {
            Version = Plugin.Version, Character = outfit.Character.name,
            Note = "@body means not owned by a wardrobe item; it can include hair/accessories. Never apply a blanket body rule.",
            Items = outfit.Items.Values.Select(x => new { x.Id, x.Coverage, x.Visible, x.Layer, Kind = x.Kind.ToString() }).ToArray(),
            Renderers = renderers
        };
        string filename = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + outfit.Character.GetInstanceID() + ".json";
        string path = Path.Combine(Plugin.ReportsPath, filename);
        File.WriteAllText(path, JsonSerializer.Serialize(report, RuleStore.Json));
        // Keep only the newest reports so diagnostics cannot fill the game folder.
        foreach (var old in new DirectoryInfo(Plugin.ReportsPath).GetFiles("*.json").OrderByDescending(f => f.Name).Skip(10)) try { old.Delete(); } catch (IOException) { }
        Plugin.Logger.LogInfo($"Outfit report for '{outfit.Character.name}': {path}");
    }
}
