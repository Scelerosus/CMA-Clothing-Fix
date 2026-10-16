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
        internal V3[] Offsets;
        internal Vector3[] Rest;
    }
    private readonly Dictionary<int, State> states = new();
    private readonly HashSet<string> warnings = new();
    private readonly HashSet<string> loggedOutfits = new();
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
            // One compact line per outfit change so problems can be diagnosed from LogOutput.log without saving a report.
            if (loggedOutfits.Add(signature))
                Plugin.Logger.LogInfo("Outfit: " + string.Join("; ", outfit.Items.Values.OrderBy(x => x.Layer).ThenBy(x => x.Kind)
                    .Select(x => $"{x.Id} [{x.Kind} L{x.Layer} {(x.Visible ? "on" : "off")} {x.Coverage}]")));
            // Reach scales with the character so tall/short models get proportional windows.
            var bodies = outfit.Renderers.Where(r => outfit.OwnerOf(r) == "@body").ToArray();
            float scale = Math.Clamp(bodies.Select(r => r.bounds.size.y).DefaultIfEmpty(1.7f).Max() / 1.7f, .6f, 1.4f);
            // shortcut: assumes a standing pose; lying poses stop masking skin entirely.
            float footY = bodies.Length == 0 ? float.NegativeInfinity : bodies.Min(r => r.bounds.min.y) + .14f * 1.7f * scale;
            var coverage = garments.ToDictionary(r => r.GetInstanceID(), r => CoverageParts(r, OriginalMesh(r)));
            bool inflating = Plugin.Mode.Value == "Inflate";
            if (inflating) Inflate(outfit, garments, coverage, bodies, signature, scale, footY, wanted, ref capturesLeft);
            foreach (var target in outfit.Renderers.OrderBy(r => outfit.OwnerOf(r) == "@body" ? 1 : 0))
            {
                if (!target.enabled || !target.gameObject.activeInHierarchy || target.forceRenderingOff) continue;
                var mesh = OriginalMesh(target); if (mesh == null || !mesh.isReadable) continue;
                var materials = target.sharedMaterials;
                string owner = outfit.OwnerOf(target);
                bool body = owner == "@body" && materials.Any(m => m != null && m.name.StartsWith("Skin_Body", StringComparison.Ordinal));
                if (!body && !garments.Contains(target)) continue;
                var item = body ? null : outfit.Items[owner];
                // While inflating, body jewellery is pushed back under the outer garment instead (a barbell must not lift a dress by centimetres).
                if (inflating && (body || item.Kind is not (ClothingOrder.Kind.UpperUnderwearAccessory or ClothingOrder.Kind.LowerUnderwearAccessory))) continue;
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
                        covers.Select(r => !body && ClothingOrder.WaistSeam(item.Layer,item.Kind,outfit.Items[outfit.OwnerOf(r)].Layer,outfit.Items[outfit.OwnerOf(r)].Kind)).ToArray(), ref capturesLeft, mode: inflating ? "Push" : null);
            }
        }
        foreach (int id in states.Keys.Where(x => !wanted.Contains(x)).ToArray()) { Release(states[id]); states.Remove(id); }
        alpha.End();
    }

    // Inflate mode: the body skin is the fixed reference and every worn garment is lifted off it; the body itself is never touched.
    private void Inflate(Outfit outfit, SkinnedMeshRenderer[] garments, Dictionary<int, (int[] parts, CoverageGeometry.Opacity[] opacities)> coverage, SkinnedMeshRenderer[] bodies, string signature, float scale, float footY, HashSet<int> wanted, ref int capturesLeft)
    {
        var skin = bodies.Where(r => r.enabled && OriginalMesh(r) != null && OriginalMesh(r).isReadable
            && r.sharedMaterials.Any(m => m != null && m.name.StartsWith("Skin_Body", StringComparison.Ordinal))).FirstOrDefault();
        if (skin == null) return;
        var skinMesh = OriginalMesh(skin);
        var materials = skin.sharedMaterials;
        var skinParts = Enumerable.Range(0, skinMesh.subMeshCount).Where(i => skinMesh.GetTopology(i) == MeshTopology.Triangles && i < materials.Length && materials[i] != null
            && (materials[i].name.StartsWith("Skin_Body", StringComparison.Ordinal) || materials[i].name.StartsWith("Pezones", StringComparison.Ordinal))).ToArray();
        if (skinParts.Length == 0) return;
        foreach (var target in garments)
        {
            var item = outfit.Items[outfit.OwnerOf(target)];
            // Body jewellery and loose accessories sit in or on the skin on purpose.
            if (item.Kind is ClothingOrder.Kind.UpperUnderwearAccessory or ClothingOrder.Kind.LowerUnderwearAccessory or ClothingOrder.Kind.Accessories) continue;
            var mesh = OriginalMesh(target);
            if (!mesh.isReadable) continue;
            var parts = Enumerable.Range(0, mesh.subMeshCount).Where(i => mesh.GetTopology(i) == MeshTopology.Triangles).ToArray();
            if (parts.Length == 0) continue;
            // The skin is the only reference: lifting over inner garments made ribbed hems ripple (0.7.1).
            var refs = new List<SkinnedMeshRenderer> { skin };
            var refParts = new List<int[]> { skinParts };
            var refOpacity = new List<CoverageGeometry.Opacity[]> { new CoverageGeometry.Opacity[skinMesh.subMeshCount] };
            // Garments worn over this one still hide its covered triangles (the dress under a hoodie); the skin is lifted over, not hidden.
            foreach (var outer in garments.Where(r => r != target && OriginalMesh(r).isReadable && coverage[r.GetInstanceID()].parts.Length > 0).OrderBy(r => r.GetInstanceID()))
            {
                var o = outfit.Items[outfit.OwnerOf(outer)];
                if (!ClothingOrder.Covers(item.Layer, (int)item.Flags, item.Kind, o.Layer, (int)o.Flags, o.Kind)) continue;
                refs.Add(outer); refParts.Add(coverage[outer.GetInstanceID()].parts); refOpacity.Add(coverage[outer.GetInstanceID()].opacities);
            }
            // Innermost layer (hosiery, underwear) should hug the skin; everything else gets a larger margin because the
            // tessellated skin shader displaces the surface a few millimetres beyond the mesh.
            bool tight = item.Layer <= 1;
            Request(scale, footY, target, refs.ToArray(), refParts.ToArray(), refOpacity.ToArray(),
                parts, signature, wanted, false, false, null, ref capturesLeft, tight ? .0007f * scale : .003f * scale, tight ? .015f * scale : 0, inflateRefs: 1);
        }
    }

    private void Request(float scale, float footY, SkinnedMeshRenderer target, SkinnedMeshRenderer[] covers, int[][] coverParts, CoverageGeometry.Opacity[][] opacities,
        int[] parts, string outfit, HashSet<int> wanted, bool body, bool jewellery, bool[] seams, ref int capturesLeft, float inflateMargin = 0, float tightReach = 0, string mode = null, int inflateRefs = 0)
    {
        mode ??= Plugin.Mode.Value;
        int id = target.GetInstanceID();
        var original = OriginalMesh(target);
        var coverOriginals = covers.Select(OriginalMesh).ToArray();
        string key = outfit + ":" + scale.ToString("F2") + ":" + Plugin.CoverReachCm.Value + ":" + Plugin.CoverDepthCm.Value + ":" + Plugin.CoverHemKeepCm.Value + ":" + mode + ":" + Plugin.PushMaxCm.Value + ":" + inflateMargin + "/" + tightReach + "/" + inflateRefs + ":" + original.GetInstanceID() + ":" + string.Join(",", parts) + ":"
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
                var keep = body ? VisibleExtremities(target, original, Extremity) : null;
                var toes = body ? VisibleExtremities(target, original, Toes) : null;
                var surfaces = covers.Select((r, i) => Capture(r, coverOriginals[i], coverParts[i], opacities[i]).surface).ToArray();
                var token = state.Cancel.Token;
                bool inflate = mode == "Inflate", push = mode == "Push", snap = mode == "Snap";
                var skin = push || snap || inflate ? CaptureSkin(target, original) : null;
                state.Job = inflate ? Task.Run(async () =>
                {
                    await Worker.WaitAsync(token);
                    try
                    {
                        var lifted = CoverageGeometry.Inflate(input.surface, input.normals, CoverageGeometry.Combine(surfaces.Take(inflateRefs), token), parts, skin,
                            Plugin.PushMaxCm.Value / 100 * scale, inflateMargin, token, 3, tightReach);
                        if (surfaces.Length <= inflateRefs) return lifted;
                        var hidden = CoverageGeometry.Mask(input.surface, input.normals, CoverageGeometry.Combine(surfaces.Skip(inflateRefs), token), parts, token, .002f, false,
                            reach: Plugin.CoverReachCm.Value / 100 * scale, depth: .035f * scale, scale: scale, keepBelowY: footY, hemKeep: Plugin.CoverHemKeepCm.Value / 100 * scale);
                        return new CoverageGeometry.Result(hidden.Kept, lifted.Removed + hidden.Removed, lifted.Total, null, lifted.Offsets);
                    }
                    finally { Worker.Release(); }
                }, token) : push ? Task.Run(async () =>
                {
                    await Worker.WaitAsync(token);
                    try { return CoverageGeometry.Push(input.surface, input.normals, CoverageGeometry.Combine(surfaces, token), parts, skin,
                        Plugin.PushMaxCm.Value / 100 * scale, .002f * scale, token); }
                    finally { Worker.Release(); }
                }, token) : Task.Run(async () =>
                {
                    await Worker.WaitAsync(token);
                    // Cloth under cloth gets the original 3.5 cm window: a bust pushes inner layers out through a coat by centimetres. Feet are exempt separately, so shoe soles no longer matter.
                    try { return CoverageGeometry.Mask(input.surface, input.normals, CoverageGeometry.Combine(surfaces, token), parts, token, body ? 0 : .002f, jewellery, skin: body,
                        reach: Plugin.CoverReachCm.Value / 100 * scale, depth: (body ? Plugin.CoverDepthCm.Value / 100 : .035f) * scale, scale: scale, keepBelowY: footY, keepVertex: keep, toeVertex: toes, hemKeep: body ? 0 : Plugin.CoverHemKeepCm.Value / 100 * scale, snapSkin: snap ? skin : null); }
                    finally { Worker.Release(); }
                }, token);
            }
            else if (state.Job.IsCompleted)
            {
                var result = state.Job.GetAwaiter().GetResult();
                state.Finished = true;
                state.ReadyAt = Time.unscaledTime + Plugin.RefreshSeconds.Value;
                if (result.Removed == 0 && (result.Offsets == null || !result.Offsets.Any(o => o != default)))
                {
                    if (state.Replacement != null) { if (target.sharedMesh == state.Replacement) target.sharedMesh = original; Object.Destroy(state.Replacement); state.Replacement = null; }
                    state.Kept = null; state.Offsets = null;
                    return;
                }
                if (result.Total == 0 || mode is "Hide" or "Snap" && body && result.Removed >= result.Total * .85f) throw new InvalidOperationException("Body coverage exceeds 85%; refusing a nearly complete removal.");
                if (target.sharedMesh != original && target.sharedMesh != state.Replacement) throw new InvalidOperationException("Target mesh changed while computing; result discarded.");
                if (result.Offsets != null)
                {
                    // Skip tiny changes: rewriting a mesh with hundreds of blend shapes every scan would cost frame time.
                    bool carriesTriangles = mode is "Snap" or "Inflate";
                    bool keptSame = !carriesTriangles || state.Kept != null && parts.All(sub => state.Kept[sub].AsSpan().SequenceEqual(result.Kept[sub]));
                    if (state.Offsets != null && state.Offsets.Length == result.Offsets.Length
                        && !result.Offsets.Where((o, i) => (o - state.Offsets[i]).LengthSquared() > 1e-6f).Any() && keptSame) return;
                    var rest = state.Rest ??= original.vertices.ToArray();
                    if (rest.Length != result.Offsets.Length) throw new InvalidOperationException("Vertex count changed while computing; result discarded.");
                    var moved = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(rest.Length);
                    for (int i = 0; i < rest.Length; i++) moved[i] = new Vector3(rest[i].x + result.Offsets[i].X, rest[i].y + result.Offsets[i].Y, rest[i].z + result.Offsets[i].Z);
                    bool first = state.Replacement == null;
                    if (first) { state.Replacement = Object.Instantiate(original); state.Replacement.name = original.name; }
                    state.Replacement.vertices = moved;
                    if (carriesTriangles && !keptSame)
                    {
                        foreach (int sub in parts)
                            state.Replacement.SetTriangles(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(result.Kept[sub]), sub, false);
                        state.Kept = result.Kept;
                    }
                    state.Replacement.RecalculateBounds();
                    target.sharedMesh = state.Replacement;
                    state.Offsets = result.Offsets;
                    if (first) Plugin.Logger.LogInfo($"Coverage ({mode}): {result.Removed}/{result.Total} moved or removed, vertices of '{target.name}' under {string.Join(", ", covers.Select(r => r.name))}.");
                    return;
                }
                if ((result.Added == null || result.Added.Length == 0) && target.sharedMesh == state.Replacement && state.Kept != null
                    && parts.All(sub => state.Kept[sub].AsSpan().SequenceEqual(result.Kept[sub]))) return;
                // Reuse the replacement: instantiating a mesh with hundreds of blend shapes on every change is costly.
                bool fresh = state.Replacement == null;
                var replacement = fresh ? Object.Instantiate(original) : state.Replacement;
                try
                {
                    if (fresh) replacement.name = original.name;
                    foreach (int sub in parts)
                        replacement.SetTriangles(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(result.Kept[sub]), sub, false);
                    target.sharedMesh = replacement;
                    state.Replacement = replacement;
                    state.Kept = result.Kept;
                }
                catch { if (fresh) Object.Destroy(replacement); throw; }
                if (!fresh) return;
                Plugin.Logger.LogInfo($"Coverage: masked {result.Removed}/{result.Total} triangles of '{target.name}' under {string.Join(", ", covers.Select(r => r.name))}.");
            }
        }
        catch (Exception ex)
        {
            Release(state);
            states.Remove(id);
            if (warnings.Add(key + ":" + ex.Message)) Plugin.Logger.LogWarning($"Coverage for '{target.name}' skipped; will retry automatically: {ex.Message}");
        }
    }

    // Hands, feet and head are usually on show and never need poke-through masking, so their skin is never hidden.
    private static readonly System.Text.RegularExpressions.Regex Extremity = new(@"(Head|Hand|Foot|Thumb|Index\d|Mid\d|Ring\d|Pinky\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex Toes = new(@"Toe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static bool[] VisibleExtremities(SkinnedMeshRenderer renderer, Mesh mesh, System.Text.RegularExpressions.Regex pattern)
    {
        var bones = renderer.bones;
        var weights = mesh.boneWeights;
        if (bones == null || weights == null || weights.Length != mesh.vertexCount) return null;
        var protectedBone = bones.Select(b => b != null && pattern.IsMatch(b.name)).ToArray();
        return weights.Select(w => w.boneIndex0 >= 0 && w.boneIndex0 < protectedBone.Length && protectedBone[w.boneIndex0]).ToArray();
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

    private static CoverageGeometry.SkinInfo CaptureSkin(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        var bones = renderer.bones; var bind = mesh.bindposes; var bw = mesh.boneWeights;
        if (bones == null || bind == null || bones.Length != bind.Length || bw == null || bw.Length != mesh.vertexCount) return null;
        var linear = new float[bones.Length][];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            var a = bones[i].localToWorldMatrix; var b = bind[i];
            // Linear part of bone.localToWorld * bindpose: translation does not affect directions.
            linear[i] = new[]
            {
                a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
                a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
                a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22
            };
        }
        var index = new int[bw.Length * 4]; var weight = new float[bw.Length * 4];
        for (int i = 0; i < bw.Length; i++)
        {
            var w = bw[i];
            index[i * 4] = w.boneIndex0; index[i * 4 + 1] = w.boneIndex1; index[i * 4 + 2] = w.boneIndex2; index[i * 4 + 3] = w.boneIndex3;
            weight[i * 4] = w.weight0; weight[i * 4 + 1] = w.weight1; weight[i * 4 + 2] = w.weight2; weight[i * 4 + 3] = w.weight3;
        }
        return new CoverageGeometry.SkinInfo(linear, index, weight);
    }

    private readonly Dictionary<(int, int, int), Skinning.MeshData> meshData = new();
    private readonly Dictionary<int, float> skinChecked = new();

    // BakeMesh stays as the reference; Managed computes the same surface in plain C# (no mesh swap, no native bake); Check runs both and logs the difference.
    private (CoverageGeometry.Surface surface, V3[] normals) Capture(SkinnedMeshRenderer renderer, Mesh original, int[] parts, CoverageGeometry.Opacity[] opacities = null)
    {
        string kind = Plugin.Skinning.Value;
        if (kind == "BakeMesh") return CaptureBaked(renderer, original, parts, opacities);
        // Check only pays for the managed surface when its result will actually be logged.
        if (kind == "Check" && skinChecked.TryGetValue(renderer.GetInstanceID(), out float lastCheck) && Time.unscaledTime < lastCheck + 30) return CaptureBaked(renderer, original, parts, opacities);
        var managed = CaptureManaged(renderer, original, parts, opacities);
        if (kind == "Managed")
        {
            if (managed != null) return managed.Value;
            if (warnings.Add("managed:" + renderer.GetInstanceID())) Plugin.Logger.LogWarning($"Managed skinning unavailable for '{renderer.name}'; using BakeMesh.");
        }
        var baked = CaptureBaked(renderer, original, parts, opacities);
        if (managed != null) CheckSkinning(renderer, original, baked.surface.Vertices, managed.Value.surface.Vertices);
        return baked;
    }

    private (CoverageGeometry.Surface surface, V3[] normals)? CaptureManaged(SkinnedMeshRenderer renderer, Mesh original, int[] parts, CoverageGeometry.Opacity[] opacities)
    {
        var bones = BoneMatrices(renderer, original);
        if (bones == null) return null;
        // Keyed by id and sizes so a mesh rewritten in place (character editor) is read again.
        var key = (original.GetInstanceID(), original.vertexCount, original.blendShapeCount);
        if (!meshData.TryGetValue(key, out var data)) meshData[key] = data = BuildMeshData(original);
        if (data == null) return null;
        var (positions, normals) = Skinning.Skin(data, i => renderer.GetBlendShapeWeight(i), bones, Influences(renderer), RootMatrix(renderer));
        var triangles = new int[original.subMeshCount][];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = parts.Contains(i) ? original.GetTriangles(i).ToArray() : Array.Empty<int>();
        var uv = opacities != null && opacities.Any(x => x != null) ? original.uv.Select(v => new V2(v.x, v.y)).ToArray() : null;
        return (new CoverageGeometry.Surface(positions, triangles, uv, opacities), normals);
    }

    private static Skinning.MeshData BuildMeshData(Mesh mesh)
    {
        var vertices = mesh.vertices; var normals = mesh.normals; var weights = mesh.boneWeights;
        int n = vertices.Length;
        if (n == 0 || weights.Length != n) return null;
        var rest = new V3[n]; var normal = normals.Length == n ? new V3[n] : Array.Empty<V3>();
        var index = new int[n * 4]; var weight = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            rest[i] = new V3(vertices[i].x, vertices[i].y, vertices[i].z);
            if (normal.Length > 0) normal[i] = new V3(normals[i].x, normals[i].y, normals[i].z);
            var w = weights[i];
            index[i * 4] = w.boneIndex0; index[i * 4 + 1] = w.boneIndex1; index[i * 4 + 2] = w.boneIndex2; index[i * 4 + 3] = w.boneIndex3;
            weight[i * 4] = w.weight0; weight[i * 4 + 1] = w.weight1; weight[i * 4 + 2] = w.weight2; weight[i * 4 + 3] = w.weight3;
        }
        // Read each blend shape frame once and keep only the non-zero deltas; the same three arrays are reused for every frame.
        var shapes = new List<Skinning.Shape>();
        var dv = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(n);
        var dn = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(n);
        var dt = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(n);
        for (int s = 0; s < mesh.blendShapeCount; s++)
        {
            var frames = new List<Skinning.ShapeFrame>();
            for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
            {
                mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                var at = new List<int>(); var delta = new List<V3>();
                var span = dv.AsSpan();
                for (int i = 0; i < n; i++)
                {
                    var d = span[i];
                    if (d.x != 0 || d.y != 0 || d.z != 0) { at.Add(i); delta.Add(new V3(d.x, d.y, d.z)); }
                }
                frames.Add(new Skinning.ShapeFrame(mesh.GetBlendShapeFrameWeight(s, f), at.ToArray(), delta.ToArray()));
            }
            if (frames.Any(f => f.Index.Length > 0)) shapes.Add(new Skinning.Shape(s, frames.ToArray()));
        }
        // boneWeights keeps at most 4 influences; with QualitySettings.skinWeights = Unlimited Unity skins with all of them, so read the full list.
        try
        {
            var counts = mesh.GetBonesPerVertex(); var all = mesh.GetAllBoneWeights();
            var start = new int[n + 1];
            int over = 0, most = 0;
            for (int i = 0; i < n; i++) { int c = counts[i]; start[i + 1] = start[i] + c; if (c > 4) over++; if (c > most) most = c; }
            if (counts.Length == n && start[n] == all.Length)
            {
                var allIndex = new int[all.Length]; var allWeight = new float[all.Length];
                for (int k = 0; k < all.Length; k++) { var bw = all[k]; allIndex[k] = bw.boneIndex; allWeight[k] = bw.weight; }
                Plugin.Logger.LogInfo($"Skinning data '{mesh.name}': {n} vertices, {shapes.Count} blend shapes with deltas, {over} vertices with more than 4 bone influences (max {most}).");
                return new Skinning.MeshData(rest, normal, allIndex, allWeight, shapes.ToArray(), start);
            }
        }
        catch (Exception ex) { Plugin.Logger.LogInfo($"Skinning data '{mesh.name}': full bone weights unavailable ({ex.GetType().Name}); using 4 influences."); }
        return new Skinning.MeshData(rest, normal, index, weight, shapes.ToArray());
    }

    // 3x4 row-major world-from-rest matrices: bone.localToWorld * bindpose.
    private static float[][] BoneMatrices(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        var bones = renderer.bones; var bind = mesh.bindposes;
        if (bones == null || bind == null || bones.Length != bind.Length) return null;
        var result = new float[bones.Length][];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            var a = bones[i].localToWorldMatrix; var b = bind[i];
            result[i] = new[]
            {
                a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22, a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03,
                a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22, a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13,
                a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22, a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23
            };
        }
        return result;
    }

    // renderer.quality 0 (Auto) defers to the project setting (1, 2, 4 or 255 = unlimited).
    private static int Influences(SkinnedMeshRenderer renderer)
    {
        int influences = (int)renderer.quality; if (influences == 0) influences = (int)QualitySettings.skinWeights;
        return Math.Clamp(influences, 1, 255);
    }

    private static float[] RootMatrix(SkinnedMeshRenderer renderer)
    {
        var root = renderer.localToWorldMatrix;
        return new[] { root.m00, root.m01, root.m02, root.m03, root.m10, root.m11, root.m12, root.m13, root.m20, root.m21, root.m22, root.m23 };
    }

    private readonly HashSet<int> probed = new();

    // One-off per renderer: recompute the managed surface with each active blend shape left out, and with weights clamped to 0..100,
    // and log which variant matches BakeMesh best. This points at the shape (or rule) that is evaluated differently.
    private void ProbeShapes(SkinnedMeshRenderer renderer, Mesh original, Skinning.MeshData data, V3[] baked)
    {
        var bones = BoneMatrices(renderer, original);
        if (bones == null || data == null) return;
        int influences = Influences(renderer); var root = RootMatrix(renderer);
        var weights = new float[original.blendShapeCount];
        for (int i = 0; i < weights.Length; i++) weights[i] = renderer.GetBlendShapeWeight(i);
        string Err(Func<int, float> weight)
        {
            var (positions, _) = Skinning.Skin(data, weight, bones, influences, root);
            var e = new float[baked.Length];
            for (int i = 0; i < e.Length; i++) e[i] = (baked[i] - positions[i]).Length() * 1000;
            Array.Sort(e);
            return $"p99.9 {e[(int)((e.Length - 1) * .999)]:F3} max {e[^1]:F3}";
        }
        string Limited(int k) { var (positions, _) = Skinning.Skin(data, i => weights[i], bones, k, root); var e = new float[baked.Length]; for (int i = 0; i < e.Length; i++) e[i] = (baked[i] - positions[i]).Length() * 1000; Array.Sort(e); return $"p99.9 {e[(int)((e.Length - 1) * .999)]:F3} max {e[^1]:F3}"; }
        var log = new System.Text.StringBuilder($"Shape probe '{renderer.name}': renderer.quality {(int)renderer.quality}, QualitySettings.skinWeights {(int)QualitySettings.skinWeights}, used {influences}; all weights as read: {Err(i => weights[i])}; weights clamped to 0..100: {Err(i => Math.Clamp(weights[i], 0, 100))}; limited to 4 influences: {Limited(4)}; all influences: {Limited(255)}.");
        foreach (var shape in data.Shapes)
        {
            if (weights[shape.ShapeIndex] == 0) continue;
            var first = shape.Frames[0];
            log.Append($"{Environment.NewLine}    '{original.GetBlendShapeName(shape.ShapeIndex)}' weight {weights[shape.ShapeIndex]:F2}, {shape.Frames.Length} frame(s), first frame weight {first.Weight:F1}, {first.Index.Length} deltas; without it: {Err(i => i == shape.ShapeIndex ? 0 : weights[i])}");
        }
        Plugin.Logger.LogInfo(log.ToString());
    }

    private void CheckSkinning(SkinnedMeshRenderer renderer, Mesh original, V3[] baked, V3[] managed)
    {
        int id = renderer.GetInstanceID();
        if (skinChecked.TryGetValue(id, out float last) && Time.unscaledTime < last + 30) return;
        skinChecked[id] = Time.unscaledTime;
        if (baked.Length != managed.Length || baked.Length == 0) { Plugin.Logger.LogWarning($"Skinning check '{renderer.name}': vertex count differs ({baked.Length} vs {managed.Length})."); return; }
        var error = new float[baked.Length];
        for (int i = 0; i < error.Length; i++) error[i] = (baked[i] - managed[i]).Length() * 1000;
        // Vertices off by more than 1 mm, and how many of those sit on a vertex moved by an active blend shape.
        int bad = 0, badOnShape = 0, activeShapes = 0;
        var touched = new bool[baked.Length];
        if (meshData.TryGetValue((original.GetInstanceID(), original.vertexCount, original.blendShapeCount), out var data) && data != null)
            foreach (var shape in data.Shapes)
                if (renderer.GetBlendShapeWeight(shape.ShapeIndex) != 0)
                {
                    activeShapes++;
                    foreach (var frame in shape.Frames) foreach (int i in frame.Index) touched[i] = true;
                }
        for (int i = 0; i < error.Length; i++) if (error[i] > 1) { bad++; if (touched[i]) badOnShape++; }
        var sorted = (float[])error.Clone();
        Array.Sort(sorted);
        Plugin.Logger.LogInfo($"Skinning check '{renderer.name}': {error.Length} vertices, error mm mean {error.Average():F3}, p99.9 {sorted[(int)((sorted.Length - 1) * .999)]:F3}, max {sorted[^1]:F3}; over 1 mm: {bad} ({badOnShape} on blend-shape vertices, {activeShapes} shapes active).");
        if (bad > 0 && probed.Add(renderer.GetInstanceID())) ProbeShapes(renderer, original, meshData.GetValueOrDefault((original.GetInstanceID(), original.vertexCount, original.blendShapeCount)), baked);
    }

    private static (CoverageGeometry.Surface surface, V3[] normals) CaptureBaked(SkinnedMeshRenderer renderer, Mesh original, int[] parts, CoverageGeometry.Opacity[] opacities = null)
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
        meshData.Clear(); skinChecked.Clear();
    }
    private static void Release(State s)
    {
        s.Cancel.Cancel();
        bool restored = true;
        try
        {
            if (s.Target != null && s.Replacement != null && s.Target.sharedMesh == s.Replacement) s.Target.sharedMesh = s.Original;
        }
        catch (Exception ex) { restored = false; Plugin.Logger.LogWarning("Could not restore coverage mesh: " + ex.Message); }
        finally
        {
            // Destroying a mesh the renderer still points at would make the body invisible.
            if (s.Replacement != null && restored) { Object.Destroy(s.Replacement); s.Replacement = null; }
            s.Kept = null;
            if (s.Job == null) s.Cancel.Dispose();
            else _ = s.Job.ContinueWith(job => { _ = job.Exception; s.Cancel.Dispose(); }, TaskScheduler.Default);
        }
    }
}
