using UnityEngine;
using Object = UnityEngine.Object;
using V3 = System.Numerics.Vector3;
using V2 = System.Numerics.Vector2;

namespace CMAClothingFix;

// Uses the native wardrobe layer order and actual posed garment surfaces.
internal sealed class CoverageFitter
{
    private static readonly SemaphoreSlim Worker = new(1);
    private sealed class State
    {
        internal string Key;
        internal SkinnedMeshRenderer Target;
        internal Mesh Original, Replacement;
        internal float ReadyAt;
        internal Task<CoverageGeometry.Result> Job;
        internal readonly CancellationTokenSource Cancel = new();
        internal bool Finished;
        internal int[][] Kept;
    }
    private readonly Dictionary<int, State> states = new();
    private readonly HashSet<string> warnings = new();
    private readonly AlphaCoverage alpha = new();
    internal int ActiveMasks => states.Values.Count(s => s.Replacement != null);
    internal int PendingMasks => states.Values.Count(s => !s.Finished);
    internal int SampledTextures => alpha.SampledTextures;

    internal Mesh OriginalMesh(SkinnedMeshRenderer renderer)
    {
        if (states.TryGetValue(renderer.GetInstanceID(), out var s) && s.Replacement != null && renderer.sharedMesh == s.Replacement) return s.Original;
        return renderer.sharedMesh;
    }

    internal void Tick(List<Outfit> outfits, bool enabled)
    {
        if (!enabled) { Restore(); return; }
        alpha.Begin();
        var wanted = new HashSet<int>();
        int capturesLeft = 1;
        foreach (var outfit in outfits)
        {
            var garments = outfit.Renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff
                && OriginalMesh(r) != null && outfit.Items.TryGetValue(outfit.OwnerOf(r), out var item) && item.Visible && item.Layer >= 0).ToArray();
            string signature = outfit.Signature();
            // Reach scales with the character so tall/short models get proportional windows.
            var bodies = outfit.Renderers.Where(r => outfit.OwnerOf(r) == "@body").ToArray();
            float scale = Math.Clamp(bodies.Select(r => r.bounds.size.y).DefaultIfEmpty(1.7f).Max() / 1.7f, .6f, 1.4f);
            // shortcut: assumes a standing pose; lying poses stop masking skin entirely.
            float footY = bodies.Length == 0 ? float.NegativeInfinity : bodies.Min(r => r.bounds.min.y) + .09f * 1.7f * scale;
            var coverage = garments.ToDictionary(r => r.GetInstanceID(), r => CoverageParts(r, OriginalMesh(r)));
            foreach (var target in outfit.Renderers.OrderBy(r => outfit.OwnerOf(r) == "@body" ? 1 : 0))
            {
                if (!target.enabled || !target.gameObject.activeInHierarchy || target.forceRenderingOff) continue;
                var mesh = OriginalMesh(target); if (mesh == null || !mesh.isReadable) continue;
                var materials = target.sharedMaterials;
                string owner = outfit.OwnerOf(target);
                bool body = owner == "@body" && materials.Any(m => m != null && m.name.StartsWith("Skin_Body", StringComparison.Ordinal));
                if (!body && !garments.Contains(target)) continue;
                var item = body ? null : outfit.Items[owner];
                var parts = Enumerable.Range(0, mesh.subMeshCount).Where(i => mesh.GetTopology(i) == MeshTopology.Triangles
                    && (!body || i < materials.Length && materials[i] != null &&
                        (materials[i].name.StartsWith("Skin_Body", StringComparison.Ordinal) || materials[i].name.StartsWith("Pezones", StringComparison.Ordinal)))).ToArray();
                var covers = garments.Where(r =>
                {
                    var outer = outfit.Items[outfit.OwnerOf(r)];
                    return r != target && (body || ClothingOrder.Covers(item.Layer, (int)item.Flags, item.Kind,
                        outer.Layer, (int)outer.Flags, outer.Kind)
                       )
                        && OriginalMesh(r).isReadable && coverage[r.GetInstanceID()].parts.Length > 0;
                }).OrderBy(r => r.GetInstanceID()).ToArray();
                if (parts.Length > 0 && covers.Length > 0)
                    Request(scale, footY, target, covers, covers.Select(r => coverage[r.GetInstanceID()].parts).ToArray(),
                        covers.Select(r => coverage[r.GetInstanceID()].opacities).ToArray(), parts, signature, wanted, body,
                        item?.Kind is ClothingOrder.Kind.UpperUnderwearAccessory or ClothingOrder.Kind.LowerUnderwearAccessory,
                        covers.Select(r => !body && ClothingOrder.WaistSeam(item.Layer,item.Kind,outfit.Items[outfit.OwnerOf(r)].Layer,outfit.Items[outfit.OwnerOf(r)].Kind)).ToArray(), ref capturesLeft);
            }
        }
        foreach (int id in states.Keys.Where(x => !wanted.Contains(x)).ToArray()) { Release(states[id]); states.Remove(id); }
        alpha.End();
    }

    private void Request(float scale, float footY, SkinnedMeshRenderer target, SkinnedMeshRenderer[] covers, int[][] coverParts, CoverageGeometry.Opacity[][] opacities,
        int[] parts, string outfit, HashSet<int> wanted, bool body, bool jewellery, bool[] seams, ref int capturesLeft)
    {
        int id = target.GetInstanceID();
        var original = OriginalMesh(target);
        var coverOriginals = covers.Select(OriginalMesh).ToArray();
        string key = outfit + ":" + scale.ToString("F2") + ":" + Plugin.CoverReachCm.Value + ":" + Plugin.CoverDepthCm.Value + ":" + original.GetInstanceID() + ":" + string.Join(",", parts) + ":"
            + string.Join(",", covers.Select((r, i) => r.GetInstanceID() + "/" + coverOriginals[i].GetInstanceID() + "/"
                + string.Join(".", coverParts[i].Select(part => part + "=" + (opacities[i][part]?.Signature ?? "opaque")))));
        wanted.Add(id);
        if (states.TryGetValue(id, out var state) && state.Key != key) { Release(state); states.Remove(id); state = null; }
        if (state == null)
        {
            state = new State { Key = key, Target = target, Original = original, ReadyAt = Time.unscaledTime };
            states[id] = state;
        }
        if (Time.unscaledTime < state.ReadyAt) return;
        if (state.Finished) { state.Finished = false; state.Job = null; }
        try
        {
            if (state.Job == null)
            {
                // Spread native mesh baking across scans to avoid a wardrobe-sized frame hitch.
                if (capturesLeft == 0 || states.Values.Any(s => s.Job != null && !s.Job.IsCompleted)
                    || states.Values.Any(s => (s.Job == null || s.Finished) && s.ReadyAt < state.ReadyAt)) return;
                capturesLeft--;
                var input = Capture(target, original, Enumerable.Range(0, original.subMeshCount).ToArray());
                var surfaces = covers.Select((r, i) => Capture(r, coverOriginals[i], coverParts[i], opacities[i]).surface).ToArray();
                var token = state.Cancel.Token;
                state.Job = Task.Run(async () =>
                {
                    await Worker.WaitAsync(token);
                    // Cloth under cloth sinks mm, not cm: a deep window reaches through insteps to shoe soles.
                    try { return CoverageGeometry.Mask(input.surface, input.normals, CoverageGeometry.Combine(surfaces, token), parts, token, body ? 0 : .002f, jewellery, skin: body,
                        reach: Plugin.CoverReachCm.Value / 100 * scale, depth: (body ? Plugin.CoverDepthCm.Value / 100 : .015f) * scale, scale: scale, keepSkinBelowY: footY); }
                    finally { Worker.Release(); }
                }, token);
            }
            else if (state.Job.IsCompleted)
            {
                var result = state.Job.GetAwaiter().GetResult();
                state.Finished = true;
                state.ReadyAt = Time.unscaledTime + Plugin.RefreshSeconds.Value;
                if (result.Removed == 0)
                {
                    if (state.Replacement != null) { if (target.sharedMesh == state.Replacement) target.sharedMesh = original; Object.Destroy(state.Replacement); state.Replacement = null; }
                    state.Kept = null;
                    return;
                }
                if (result.Total == 0 || body && result.Removed >= result.Total * .85f) throw new InvalidOperationException("Body coverage exceeds 85%; refusing a nearly complete removal.");
                if (target.sharedMesh != original && target.sharedMesh != state.Replacement) throw new InvalidOperationException("Target mesh changed while computing; result discarded.");
                if ((result.Added == null || result.Added.Length == 0) && target.sharedMesh == state.Replacement && state.Kept != null
                    && parts.All(sub => state.Kept[sub].AsSpan().SequenceEqual(result.Kept[sub]))) return;
                var previous = state.Replacement;
                var replacement = Object.Instantiate(original);
                try
                {
                    replacement.name = original.name;

                    foreach (int sub in parts)
                        replacement.SetTriangles(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(result.Kept[sub]), sub, false);
                    target.sharedMesh = replacement;
                    state.Replacement = replacement;
                    state.Kept = result.Kept;
                }
                catch { Object.Destroy(replacement); throw; }
                if (previous != null) Object.Destroy(previous);
                else Plugin.Logger.LogInfo($"Coverage: masked {result.Removed}/{result.Total} triangles of '{target.name}' under {covers.Length} outer renderer(s).");
            }
        }
        catch (Exception ex)
        {
            Release(state);
            states.Remove(id);
            if (warnings.Add(key + ":" + ex.Message)) Plugin.Logger.LogWarning($"Coverage for '{target.name}' skipped; will retry automatically: {ex.Message}");
        }
    }

    private (int[] parts, CoverageGeometry.Opacity[] opacities) CoverageParts(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        var materials = renderer.sharedMaterials;
        var parts = new List<int>();
        var opacities = new CoverageGeometry.Opacity[mesh.subMeshCount];
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (i >= materials.Length || materials[i] == null || mesh.GetTopology(i) != MeshTopology.Triangles) continue;
            var mat = materials[i];
            if (!mat.HasProperty("_SurfaceType")) continue;
            bool transparent = mat.GetFloat("_SurfaceType") >= .5f;
            bool cutout = mat.HasProperty("_AlphaCutoffEnable") && mat.GetFloat("_AlphaCutoffEnable") >= .5f;
            if (!transparent && !cutout) { parts.Add(i); continue; }
            float baseAlpha = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").a : 1;
            float cutoff = mat.HasProperty("_AlphaCutoff") ? mat.GetFloat("_AlphaCutoff") : .5f;
            float required = transparent ? .995f : Math.Clamp(cutoff + .05f, 0, 1);
            if (!(baseAlpha >= required)) continue;
            var texture = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") : null;
            if (texture != null)
            {
                // shortcut: UV0 material mapping only; add other mappings when an actual garment needs them.
                if (mat.HasProperty("_UVBase") && Math.Abs(mat.GetFloat("_UVBase")) > .01f) continue;
                var image = alpha.Read(texture);
                if (image == null) continue;
                var scale = mat.GetTextureScale("_BaseColorMap"); var offset = mat.GetTextureOffset("_BaseColorMap");
                opacities[i] = new CoverageGeometry.Opacity(image, new V2(scale.x, scale.y), new V2(offset.x, offset.y),
                    required / baseAlpha, (int)texture.wrapModeU, (int)texture.wrapModeV, texture.GetInstanceID());
            }
            parts.Add(i);
        }
        return (parts.ToArray(), opacities);
    }

    private static (CoverageGeometry.Surface surface, V3[] normals) Capture(SkinnedMeshRenderer renderer, Mesh original, int[] parts, CoverageGeometry.Opacity[] opacities = null)
    {
        var baked = new Mesh();
        var previous = renderer.sharedMesh;
        try
        {
            renderer.sharedMesh = original;
            renderer.BakeMesh(baked, false);
            var matrix = renderer.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            V3 Point(Vector3 v) => new(matrix.m00 * v.x + matrix.m01 * v.y + matrix.m02 * v.z + matrix.m03,
                matrix.m10 * v.x + matrix.m11 * v.y + matrix.m12 * v.z + matrix.m13,
                matrix.m20 * v.x + matrix.m21 * v.y + matrix.m22 * v.z + matrix.m23);
            V3 Normal(Vector3 v) => new(normalMatrix.m00 * v.x + normalMatrix.m01 * v.y + normalMatrix.m02 * v.z,
                normalMatrix.m10 * v.x + normalMatrix.m11 * v.y + normalMatrix.m12 * v.z,
                normalMatrix.m20 * v.x + normalMatrix.m21 * v.y + normalMatrix.m22 * v.z);
            var vertices = baked.vertices.Select(Point).ToArray();
            var normals = baked.normals.Select(Normal).ToArray();
            var triangles = new int[original.subMeshCount][];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = parts.Contains(i) ? original.GetTriangles(i).ToArray() : Array.Empty<int>();
            var uv = opacities != null && opacities.Any(x => x != null) ? original.uv.Select(v => new V2(v.x, v.y)).ToArray() : null;
            return (new CoverageGeometry.Surface(vertices, triangles, uv, opacities), normals);
        }
        finally { renderer.sharedMesh = previous; Object.Destroy(baked); }
    }

    internal void Restore()
    {
        foreach (var state in states.Values) Release(state);
        states.Clear();
        alpha.Clear();
    }
    private static void Release(State s)
    {
        s.Cancel.Cancel();
        try
        {
            if (s.Target != null && s.Replacement != null && s.Target.sharedMesh == s.Replacement) s.Target.sharedMesh = s.Original;
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Could not restore coverage mesh: " + ex.Message); }
        finally
        {
            if (s.Replacement != null) { Object.Destroy(s.Replacement); s.Replacement = null; }
            s.Kept = null;
            if (s.Job == null) s.Cancel.Dispose();
            else _ = s.Job.ContinueWith(job => { _ = job.Exception; s.Cancel.Dispose(); }, TaskScheduler.Default);
        }
    }
}
