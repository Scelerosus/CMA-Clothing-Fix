using UnityEngine;
using Object = UnityEngine.Object;

namespace CMAClothingFix;

internal sealed class VisibilityLedger
{
    private sealed class Change
    {
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Original, Replacement;
        internal bool OriginalForceOff;
        internal string Signature;
    }
    private readonly Dictionary<int, Change> changes = new();
    private readonly HashSet<int> desired = new();
    private readonly HashSet<string> warned = new();

    internal void Begin() => desired.Clear();
    internal void End()
    {
        foreach (int key in changes.Keys.Where(x => !desired.Contains(x)).ToArray())
        {
            RestoreOne(changes[key]);
            changes.Remove(key);
        }
    }
    internal Mesh OriginalMesh(SkinnedMeshRenderer renderer)
    {
        if (changes.TryGetValue(renderer.GetInstanceID(), out var change) && change.Replacement != null && renderer.sharedMesh == change.Replacement)
            return change.Original;
        return renderer.sharedMesh;
    }

    internal void Apply(Outfit outfit, ClothingRule[] rules)
    {
        var targets = new Dictionary<int, (SkinnedMeshRenderer renderer, HashSet<int> submeshes, bool whole)>();
        foreach (var rule in rules)
        {
            try
            {
                if (!rule.RequiredItems.All(id => outfit.Items.TryGetValue(id, out var item) && item.Visible)) continue;
                if (rule.TargetItem != "@body" && (!outfit.Items.TryGetValue(rule.TargetItem, out var target) || !target.Visible)) continue;
                var matches = outfit.Renderers.Where(r => outfit.OwnerOf(r) == rule.TargetItem &&
                    Wardrobe.RendererPath(outfit.Character.transform, r.transform) == rule.RendererPath).ToArray();
                if (matches.Length != 1) { Warn(rule, "renderer path missing or ambiguous"); continue; }
                var renderer = matches[0];
                var mesh = OriginalMesh(renderer);
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (mesh == null || mesh.name != rule.MeshName || mesh.vertexCount != rule.VertexCount)
                { Warn(rule, "mesh signature differs"); continue; }
                if (rule.Submeshes.Any(s => s >= mesh.subMeshCount)) { Warn(rule, "submesh index out of range"); continue; }
                if (rule.Submeshes.Length > 0 && (!mesh.isReadable || rule.Submeshes.Any(s => mesh.GetTopology(s) != MeshTopology.Triangles)))
                { Warn(rule, "submesh masking requires a readable triangle mesh"); continue; }
                int key = renderer.GetInstanceID();
                if (!targets.TryGetValue(key, out var request)) request = (renderer, new HashSet<int>(), false);
                request.whole |= rule.HideWholeRenderer;
                request.submeshes.UnionWith(rule.Submeshes);
                targets[key] = request;
            }
            catch (Exception ex) { Warn(rule, ex.Message); }
        }
        foreach (var request in targets.Values)
        {
            var renderer = request.renderer;
            int key = renderer.GetInstanceID();
            var original = OriginalMesh(renderer);
            string signature = original.GetInstanceID() + ":" + request.whole + ":" + string.Join(",", request.submeshes.OrderBy(x => x));
            desired.Add(key);
            if (changes.TryGetValue(key, out var existing))
            {
                if (existing.Signature == signature && (existing.Replacement == null || renderer.sharedMesh == existing.Replacement))
                {
                    if (existing.Replacement == null) renderer.forceRenderingOff = true;
                    continue;
                }
                RestoreOne(existing);
                changes.Remove(key);
            }
            var change = new Change { Renderer = renderer, Original = renderer.sharedMesh, OriginalForceOff = renderer.forceRenderingOff, Signature = signature };
            try
            {
                if (request.whole) renderer.forceRenderingOff = true;
                else
                {
                    // Clone preserves skin weights, bind poses and blend shapes. Only selected index buffers are emptied.
                    change.Replacement = Object.Instantiate(change.Original);
                    change.Replacement.name = change.Original.name;
                    foreach (int submesh in request.submeshes)
                        change.Replacement.SetTriangles(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(0), submesh, false);
                    renderer.sharedMesh = change.Replacement;
                }
                changes[key] = change;
            }
            catch (Exception ex)
            {
                RestoreOne(change);
                if (warned.Add(ex.Message)) Plugin.Logger.LogWarning("Clipping rule application failed: " + ex.Message);
            }
        }
    }

    private void Warn(ClothingRule rule, string reason)
    {
        string message = $"Rule '{rule.Name}' skipped: {reason}.";
        if (warned.Add(message)) Plugin.Logger.LogWarning(message);
    }

    internal void Restore()
    {
        foreach (var change in changes.Values) RestoreOne(change);
        changes.Clear();
    }

    private static void RestoreOne(Change change)
    {
        try
        {
            if (change.Renderer != null)
            {
                if (change.Replacement != null)
                {
                    // A wardrobe rebuild or another mod may already have replaced the mesh.
                    if (change.Renderer.sharedMesh == change.Replacement) change.Renderer.sharedMesh = change.Original;
                }
                else change.Renderer.forceRenderingOff = change.OriginalForceOff;
            }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Could not restore renderer: " + ex.Message); }
        finally { if (change.Replacement != null) Object.Destroy(change.Replacement); }
    }
}
