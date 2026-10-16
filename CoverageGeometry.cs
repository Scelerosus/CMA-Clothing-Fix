using System.Numerics;

namespace CMAClothingFix;

// Pure managed geometry: no Unity or IL2CPP calls on the worker thread.
internal static class CoverageGeometry
{
    internal sealed record AlphaImage(int Width, int Height, byte[] Pixels, uint Revision = 0);
    internal sealed record Opacity(AlphaImage Image, Vector2 Scale, Vector2 Offset, float Minimum, int WrapU, int WrapV, int TextureId = 0)
    {
        internal string Signature => $"{TextureId}:{Image?.Revision ?? 0}:{Minimum:R}:{Scale.X:R}:{Scale.Y:R}:{Offset.X:R}:{Offset.Y:R}:{WrapU}:{WrapV}";
        internal bool Covers(Vector2 uv)
        {
            if (Image == null || Image.Pixels == null || Image.Width <= 0 || Image.Height <= 0 || Image.Pixels.Length != (long)Image.Width * Image.Height
                || !float.IsFinite(Minimum) || Minimum > 1) return false;
            uv = uv * Scale + Offset;
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y) || WrapU is < 0 or > 3 || WrapV is < 0 or > 3) return false;
            float Address(float v, int wrap) => wrap switch
            {
                0 => v - MathF.Floor(v),
                2 => 1 - MathF.Abs((v - 2 * MathF.Floor(v / 2)) - 1),
                3 => Math.Clamp(MathF.Abs(v), 0, 1),
                _ => Math.Clamp(v, 0, 1)
            };
            int Index(int i, int size, int wrap) => wrap == 0 ? (i % size + size) % size : Math.Clamp(i, 0, size - 1);
            int x = (int)MathF.Floor(Address(uv.X, WrapU) * Image.Width - .5f);
            int y = (int)MathF.Floor(Address(uv.Y, WrapV) * Image.Height - .5f);
            float required = Math.Clamp(Minimum, 0, 1) * 255;
            // Requiring the full bilinear footprint preserves alpha borders instead of filling them in.
            for (int iy = y; iy <= y + 1; iy++) for (int ix = x; ix <= x + 1; ix++)
                if (Image.Pixels[Index(iy, Image.Height, WrapV) * Image.Width + Index(ix, Image.Width, WrapU)] < required) return false;
            return true;
        }
    }
    internal sealed record Surface(Vector3[] Vertices, int[][] Triangles, Vector2[] UV = null, Opacity[] Opacities = null, float[] MinimumTargetY = null);
    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector2 UVA, Vector2 UVB, Vector2 UVC, Opacity Opacity, float MinimumTargetY)
    {
        internal Vector3 Min => Vector3.Min(A, Vector3.Min(B, C));
        internal Vector3 Max => Vector3.Max(A, Vector3.Max(B, C));
        internal Vector3 Center => (A + B + C) / 3;
    }
    private sealed class Node
    {
        internal Vector3 Min, Max;
        internal Node Left, Right;
        internal int Start, Count;
    }
    internal sealed record NewVertex(int A, int B, int C, Vector3 Weights);
    internal sealed record Result(int[][] Kept, int Removed, int Total, NewVertex[] Added = null, Vector3[] Offsets = null);
    // BoneLinear[i] is the 3x3 world-from-rest linear part (row-major) of bone i; Index/Weight hold 4 influences per vertex.
    internal sealed record SkinInfo(float[][] BoneLinear, int[] Index, float[] Weight);

    internal static Surface Combine(IEnumerable<Surface> surfaces, CancellationToken cancel = default)
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var triangles = new List<int[]>(); var opacities = new List<Opacity>();
        var minimumY = new List<float>();
        foreach (var surface in surfaces)
        {
            cancel.ThrowIfCancellationRequested();
            int offset = vertices.Count;
            vertices.AddRange(surface.Vertices);
            uv.AddRange(surface.UV != null && surface.UV.Length == surface.Vertices.Length ? surface.UV : Enumerable.Repeat(new Vector2(float.NaN), surface.Vertices.Length));
            for (int sub = 0; sub < surface.Triangles.Length; sub++)
            {
                cancel.ThrowIfCancellationRequested();
                triangles.Add(surface.Triangles[sub].Select(index => index + offset).ToArray());
                opacities.Add(surface.Opacities != null && sub < surface.Opacities.Length ? surface.Opacities[sub] : null);
                minimumY.Add(surface.MinimumTargetY != null && sub < surface.MinimumTargetY.Length ? surface.MinimumTargetY[sub] : float.NegativeInfinity);
            }
        }
        return new Surface(vertices.ToArray(), triangles.ToArray(), uv.ToArray(), opacities.ToArray(),minimumY.ToArray());
    }

    private static (Triangle[] triangles, Node root) BuildBvh(Surface cover, CancellationToken cancel)
    {
        var coverTriangles = new List<Triangle>();
        for (int part = 0; part < cover.Triangles.Length; part++)
        {
            var sub = cover.Triangles[part];
            var opacity = cover.Opacities != null && part < cover.Opacities.Length ? cover.Opacities[part] : null;
            if (opacity != null && (cover.UV == null || cover.UV.Length != cover.Vertices.Length)) continue;
            Vector2 UV(int index) => opacity == null ? Vector2.Zero : cover.UV[index];
            for (int i = 0; i < sub.Length; i += 3)
            {
                if ((i & 1023) == 0) cancel.ThrowIfCancellationRequested();
                int a = sub[i], b = sub[i + 1], c = sub[i + 2];
                coverTriangles.Add(new Triangle(cover.Vertices[a], cover.Vertices[b], cover.Vertices[c], UV(a), UV(b), UV(c), opacity,
                    cover.MinimumTargetY != null && part < cover.MinimumTargetY.Length ? cover.MinimumTargetY[part] : float.NegativeInfinity));
            }
        }
        var triangles = coverTriangles.ToArray();
        if (triangles.Length == 0) return (triangles, null);
        Node Build(int start, int count)
        {
            cancel.ThrowIfCancellationRequested();
            var node = new Node { Start = start, Count = count, Min = new Vector3(float.PositiveInfinity), Max = new Vector3(float.NegativeInfinity) };
            for (int i = start; i < start + count; i++) { node.Min = Vector3.Min(node.Min, triangles[i].Min); node.Max = Vector3.Max(node.Max, triangles[i].Max); }
            if (count <= 12) return node;
            Vector3 size = node.Max - node.Min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            float Axis(Vector3 p) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
            Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) => Axis(a.Center).CompareTo(Axis(b.Center))));
            int half = count / 2;
            node.Left = Build(start, half); node.Right = Build(start + half, count - half);
            return node;
        }
        var root = Build(0, triangles.Length);
        return (triangles, root);
    }

    internal static Result Mask(Surface target, Vector3[] normals, Surface cover, int[] targetSubmeshes, CancellationToken cancel, float edgeInset = 0, bool bodyJewellery = false, bool refine = false, bool skin = false, float reach = .055f, float depth = .035f, float scale = 1, float keepBelowY = float.NegativeInfinity, bool[] keepVertex = null, bool[] toeVertex = null, float hemKeep = 0, SkinInfo snapSkin = null)
    {
        cancel.ThrowIfCancellationRequested();
        var (triangles, root) = BuildBvh(cover, cancel);
        if (triangles.Length == 0) return new Result(target.Triangles, 0, 0);
        float cast = reach + depth;
        bool Hit(Node node, Vector3 origin, Vector3 direction, float distance = -1, float targetY = float.PositiveInfinity)
        {
            if (distance < 0) distance = cast;
            if (!BoxHit(node.Min, node.Max, origin, direction, distance)) return false;
            if (node.Left != null) return Hit(node.Left, origin, direction, distance,targetY) || Hit(node.Right, origin, direction, distance,targetY);
            for (int i = node.Start; i < node.Start + node.Count; i++)
                if (targetY >= triangles[i].MinimumTargetY && TriangleHit(triangles[i], origin, direction, distance)) return true;
            return false;
        }
        Vector3 Unit(Vector3 normal)
        {
            float length = normal.LengthSquared();
            return float.IsFinite(length) && length > 1e-12f ? normal / MathF.Sqrt(length) : Vector3.Zero;
        }
        bool Covered(Vector3 p, Vector3 normal)
        {
            normal = Unit(normal);
            if (normal == Vector3.Zero || !Hit(root, p + normal * reach, -normal, cast, p.Y)) return false;
            if (!skin) return true;
            // Keep exposed skin beside openings, including curved cleavage and underbust edges.
            var tangent = Unit(Vector3.Cross(normal, Math.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX)) * .004f * scale;
            var bitangent = Vector3.Cross(normal, tangent);
            bool Nearby(Vector3 offset) => Hit(root, p + offset + normal * reach, -normal, cast, (p + offset).Y);
            return Nearby(tangent) && Nearby(-tangent) && Nearby(bitangent) && Nearby(-bitangent);
        }
        var jewelleryAxes = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        bool JewelleryCovered(Vector3 p) => jewelleryAxes.Any(axis => Hit(root, p + axis * .0075f, -axis, .015f,p.Y));
        var vertexNormals = normals.Length == target.Vertices.Length ? normals.Select(Unit).ToArray() : Array.Empty<Vector3>();
        var vertexCoverage = new sbyte[vertexNormals.Length];
        Vector3 NormalAt(int index, Vector3 fallback) => vertexNormals.Length > 0 && vertexNormals[index] != Vector3.Zero ? vertexNormals[index] : fallback;
        bool CoveredVertex(int index, Vector3 fallback)
        {
            if (vertexNormals.Length == 0 || vertexNormals[index] == Vector3.Zero) return Covered(target.Vertices[index], fallback);
            if (vertexCoverage[index] == 0) vertexCoverage[index] = (sbyte)(Covered(target.Vertices[index], vertexNormals[index]) ? 1 : -1);
            return vertexCoverage[index] == 1;
        }
        var selected = targetSubmeshes.ToHashSet();
        int removed = 0, total = 0;
        var added = new List<NewVertex>();
        var kept = new int[target.Triangles.Length][];
        for (int sub = 0; sub < kept.Length; sub++)
        {
            int[] source = target.Triangles[sub];
            if (!selected.Contains(sub)) { kept[sub] = source; continue; }
            var output = new List<int>(source.Length);
            for (int i = 0; i < source.Length; i += 3)
            {
                if ((i & 255) == 0) cancel.ThrowIfCancellationRequested();
                total++;
                int ia = source[i], ib = source[i + 1], ic = source[i + 2];
                Vector3 a = target.Vertices[ia], b = target.Vertices[ib], c = target.Vertices[ic];
                Vector3 face = Unit(Vector3.Cross(b - a, c - a));
                bool hide = false;
                // Feet: shoe openings and a thin instep make the normal test unreliable, so skin and hosiery there are never masked.
                bool toe = toeVertex != null && (toeVertex[ia] || toeVertex[ib] || toeVertex[ic]);
                // Toes are the one foot part that does poke through shoes, so they stay maskable.
                if (face != Vector3.Zero && (toe || !((a.Y + b.Y + c.Y) / 3 < keepBelowY)) && !(keepVertex != null && (keepVertex[ia] || keepVertex[ib] || keepVertex[ic])))
                {
                    var na = NormalAt(ia, face); var nb = NormalAt(ib, face); var nc = NormalAt(ic, face);
                    var center = (a + b + c) / 3;
                    bool Sample(Vector3 p, Vector3 n) => Covered(p,n)
                        || edgeInset > 0 && Covered(p,face) || bodyJewellery && JewelleryCovered(p);
                    Vector3 Inset(Vector3 p) => Vector3.Lerp(p, center,
                        Math.Clamp(edgeInset / Math.Max(Vector3.Distance(p, center), 1e-6f), 0, .1f));
                    // shortcut: cloth edges allow at most 2 mm overmasking; use mesh subdivision for larger boundary errors.
                    bool Corner(int index, Vector3 p, Vector3 n) => edgeInset <= 0 && !bodyJewellery ? CoveredVertex(index, face) : Sample(Inset(p), n);
                    // Project along the local skin normal. Every sample stays inside the triangle, including thin straps.
                    hide = Corner(ia, a, na) && Corner(ib, b, nb) && Corner(ic, c, nc)
                        && Sample(Inset((a + b) / 2), na + nb) && Sample(Inset((b + c) / 2), nb + nc)
                        && Sample(Inset((c + a) / 2), nc + na) && Sample(center, na + nb + nc)
                        && Sample((a * 2 + b + c) / 4, na * 2 + nb + nc)
                        && Sample((a + b * 2 + c) / 4, na + nb * 2 + nc)
                        && Sample((a + b + c * 2) / 4, na + nb + nc * 2)
                        // A garment's lower hem is thick and flared, so cloth right under it stays visible instead of ending in a jagged cut.
                        && (hemKeep <= 0 || Covered(a - Vector3.UnitY * hemKeep, na) && Covered(b - Vector3.UnitY * hemKeep, nb) && Covered(c - Vector3.UnitY * hemKeep, nc));
                }
                if (hide) removed++;
                else if (refine && face != Vector3.Zero)
                {
                    var na = NormalAt(ia,face); var nb = NormalAt(ib,face); var nc = NormalAt(ic,face);
                    Vector3 Point(Vector3 w) => a*w.X+b*w.Y+c*w.Z;
                    bool Sample(Vector3 w) => Covered(Point(w),na*w.X+nb*w.Y+nc*w.Z) || Covered(Point(w),face);
                    int Index(Vector3 w)
                    {
                        if(w==Vector3.UnitX)return ia; if(w==Vector3.UnitY)return ib; if(w==Vector3.UnitZ)return ic;
                        if(added.Count>=20000)throw new InvalidOperationException("Boundary subdivision vertex budget exceeded.");
                        added.Add(new NewVertex(ia,ib,ic,w)); return target.Vertices.Length+added.Count-1;
                    }
                    bool changed = false;
                    void Split(Vector3 u,Vector3 v,Vector3 w,int depth)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var uv=(u+v)/2;var vw=(v+w)/2;var wu=(w+u)/2;
                        var points=new[]{u,v,w,uv,vw,wu,(u+v+w)/3,(u*2+v+w)/4,(u+v*2+w)/4,(u+v+w*2)/4};
                        int count=points.Count(Sample);
                        if(count==points.Length){changed=true;return;}
                        float edge=Math.Max(Vector3.Distance(Point(u),Point(v)),Math.Max(Vector3.Distance(Point(v),Point(w)),Vector3.Distance(Point(w),Point(u))));
                        if(count>0 && depth<3 && edge>.003f)
                        {
                            Split(u,uv,wu,depth+1);Split(uv,v,vw,depth+1);Split(wu,vw,w,depth+1);Split(uv,vw,wu,depth+1);return;
                        }
                        output.Add(Index(u));output.Add(Index(v));output.Add(Index(w));
                    }
                    Split(Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,0);
                    if(changed)removed++;
                }
                else { output.Add(ia); output.Add(ib); output.Add(ic); }
            }
            kept[sub] = output.SequenceEqual(source) ? source : output.ToArray();
        }
        Vector3[] snapped = null;
        if (snapSkin != null && snapSkin.Index.Length == target.Vertices.Length * 4 && vertexNormals.Length == target.Vertices.Length)
        {
            // Edge snapping: a covered vertex of a visible boundary triangle slides along its edge to where coverage ends,
            // so the cut follows the garment edge instead of whole-triangle stair steps. Vertex count and skinning stay untouched.
            bool Exempt(int i) => (keepVertex != null && keepVertex[i]) || target.Vertices[i].Y < keepBelowY && !(toeVertex != null && toeVertex[i]);
            var state = new sbyte[target.Vertices.Length];
            bool Cov(int i) => state[i] != 0 ? state[i] > 0 : (state[i] = (sbyte)(!Exempt(i) && CoveredVertex(i, Vector3.Zero) ? 1 : -1)) > 0;
            var sum = new Vector3[target.Vertices.Length]; var count = new int[target.Vertices.Length];
            void Edge(int x, int y)
            {
                if (!Cov(x) || Cov(y)) return;
                cancel.ThrowIfCancellationRequested();
                Vector3 px = target.Vertices[x], py = target.Vertices[y];
                float lo = 0, hi = 1;
                for (int step = 0; step < 6; step++)
                {
                    float mid = (lo + hi) / 2;
                    if (Covered(Vector3.Lerp(px, py, mid), vertexNormals[x] + (vertexNormals[y] - vertexNormals[x]) * mid)) lo = mid; else hi = mid;
                }
                sum[x] += (py - px) * lo; count[x]++;
            }
            foreach (int sub in selected)
                for (int i = 0; i + 2 < kept[sub].Length; i += 3)
                {
                    int a = kept[sub][i], b = kept[sub][i + 1], c = kept[sub][i + 2];
                    Edge(a, b); Edge(a, c); Edge(b, a); Edge(b, c); Edge(c, a); Edge(c, b);
                }
            snapped = new Vector3[target.Vertices.Length];
            for (int v = 0; v < snapped.Length; v++)
                if (count[v] > 0 && Unskin(snapSkin, v, sum[v] / count[v], out var rest)) snapped[v] = rest;
        }
        return new Result(kept, removed, total, added.ToArray(), snapped);
    }


    // Instead of deleting triangles, move target vertices that poke out through an outer garment back under its surface.
    // Offsets are in mesh rest space; topology, weights and blend shapes are untouched.
    internal static Result Push(Surface target, Vector3[] normals, Surface cover, int[] targetSubmeshes, SkinInfo skin, float maxPush, float margin, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var (triangles, root) = BuildBvh(cover, cancel);
        int n = target.Vertices.Length;
        if (triangles.Length == 0 || normals.Length != n || skin == null || skin.Index.Length != n * 4) return new Result(target.Triangles, 0, 0);
        var used = new bool[n];
        foreach (int sub in targetSubmeshes) foreach (int index in target.Triangles[sub]) used[index] = true;
        var offsets = new Vector3[n];
        int moved = 0, total = 0;
        for (int v = 0; v < n; v++)
        {
            if (!used[v]) continue;
            total++;
            if ((v & 255) == 0) cancel.ThrowIfCancellationRequested();
            float length = normals[v].LengthSquared();
            if (!float.IsFinite(length) || length < 1e-12f) continue;
            var normal = normals[v] / MathF.Sqrt(length);
            var p = target.Vertices[v];
            // The closest cover surface along the normal, either side of the vertex, ignoring far sides of thin parts such as fingers.
            float best = float.NaN;
            var ignored = Vector3.Zero;
            NearestHit(root, triangles, p + normal * maxPush, -normal, 2 * maxPush, maxPush, p.Y, ref best, ref ignored);
            if (float.IsNaN(best)) continue;
            float need = best - maxPush + margin;
            if (need <= 0) continue;
            if (!Unskin(skin, v, -normal * need, out var rest)) continue;
            offsets[v] = rest; moved++;
        }
        return new Result(target.Triangles, moved, total, null, offsets);
    }

    // Mirror of Push for garments: the body is always the innermost layer, so a garment vertex that sank below the skin surface
    // is moved out along the skin's outward normal until it sits `margin` above it. No layer rules and nothing is deleted.
    internal static Result Inflate(Surface target, Vector3[] normals, Surface body, int[] targetSubmeshes, SkinInfo skin, float maxPush, float margin, CancellationToken cancel, int smoothing = 3, float tightReach = 0)
    {
        cancel.ThrowIfCancellationRequested();
        var (triangles, root) = BuildBvh(body, cancel);
        int n = target.Vertices.Length;
        if (triangles.Length == 0 || normals.Length != n || skin == null || skin.Index.Length != n * 4) return new Result(target.Triangles, 0, 0);
        var used = new bool[n];
        foreach (int sub in targetSubmeshes) foreach (int index in target.Triangles[sub]) used[index] = true;
        var world = new Vector3[n];
        int total = 0;
        for (int v = 0; v < n; v++)
        {
            if (!used[v]) continue;
            total++;
            if ((v & 255) == 0) cancel.ThrowIfCancellationRequested();
            float length = normals[v].LengthSquared();
            if (!float.IsFinite(length) || length < 1e-12f) continue;
            var normal = normals[v] / MathF.Sqrt(length);
            var p = target.Vertices[v];
            float best = float.NaN; var hit = Vector3.Zero;
            NearestHit(root, triangles, p - normal * maxPush, normal, 2 * maxPush, maxPush, float.PositiveInfinity, ref best, ref hit);
            if (float.IsNaN(best)) continue;
            float hl = hit.LengthSquared();
            if (!float.IsFinite(hl) || hl < 1e-18f) continue;
            hit /= MathF.Sqrt(hl);
            // Garment and skin must agree on which side is outside; otherwise (inverted normals) leave the vertex alone.
            if (Vector3.Dot(hit, normal) <= 0) continue;
            // Signed height of the vertex above the skin surface plane at the hit point.
            float above = -(best - maxPush) * Vector3.Dot(hit, normal);
            float need = margin - above;
            // Tight garments (hosiery, underwear) are also pulled in to the margin when they hover within tightReach of the skin.
            if (need > maxPush + margin || need <= 0 && !(tightReach > 0 && -need <= tightReach)) continue;
            if (need == 0) continue;
            world[v] = hit * need;
        }
        // Spread each lift to neighbouring vertices with a 0.7 falloff per ring. Only larger values replace smaller ones,
        // so a vertex never gets less than it needs; this removes stair steps where lifted and untouched vertices meet.
        if (smoothing > 0)
        {
            var edges = new List<(int a, int b)>();
            foreach (int sub in targetSubmeshes)
            {
                var t = target.Triangles[sub];
                for (int i = 0; i + 2 < t.Length; i += 3) { edges.Add((t[i], t[i + 1])); edges.Add((t[i + 1], t[i + 2])); edges.Add((t[i + 2], t[i])); }
            }
            for (int pass = 0; pass < smoothing; pass++)
            {
                cancel.ThrowIfCancellationRequested();
                var next = (Vector3[])world.Clone();
                foreach (var (a, b) in edges)
                {
                    var fromB = world[b] * .7f; if (fromB.LengthSquared() > next[a].LengthSquared()) next[a] = fromB;
                    var fromA = world[a] * .7f; if (fromA.LengthSquared() > next[b].LengthSquared()) next[b] = fromA;
                }
                world = next;
            }
        }
        var offsets = new Vector3[n];
        int moved = 0;
        for (int v = 0; v < n; v++)
            if (world[v] != Vector3.Zero && Unskin(skin, v, world[v], out var rest)) { offsets[v] = rest; moved++; }
        return new Result(target.Triangles, moved, total, null, offsets);
    }

    private static bool Unskin(SkinInfo skin, int vertex, Vector3 world, out Vector3 rest)
    {
        rest = default;
        Span<float> m = stackalloc float[9];
        for (int k = 0; k < 4; k++)
        {
            float w = skin.Weight[vertex * 4 + k]; int bone = skin.Index[vertex * 4 + k];
            if (w <= 0 || bone < 0 || bone >= skin.BoneLinear.Length || skin.BoneLinear[bone] == null) continue;
            for (int i = 0; i < 9; i++) m[i] += w * skin.BoneLinear[bone][i];
        }
        float c00 = m[4] * m[8] - m[5] * m[7], c01 = m[5] * m[6] - m[3] * m[8], c02 = m[3] * m[7] - m[4] * m[6];
        float det = m[0] * c00 + m[1] * c01 + m[2] * c02;
        if (!float.IsFinite(det) || Math.Abs(det) < 1e-9f) return false;
        float inv = 1 / det;
        rest = new Vector3(
            (c00 * world.X + (m[2] * m[7] - m[1] * m[8]) * world.Y + (m[1] * m[5] - m[2] * m[4]) * world.Z) * inv,
            (c01 * world.X + (m[0] * m[8] - m[2] * m[6]) * world.Y + (m[2] * m[3] - m[0] * m[5]) * world.Z) * inv,
            (c02 * world.X + (m[1] * m[6] - m[0] * m[7]) * world.Y + (m[0] * m[4] - m[1] * m[3]) * world.Z) * inv);
        return float.IsFinite(rest.X) && float.IsFinite(rest.Y) && float.IsFinite(rest.Z);
    }

    private static void NearestHit(Node node, Triangle[] triangles, Vector3 o, Vector3 d, float distance, float wanted, float targetY, ref float best, ref Vector3 hitNormal)
    {
        if (!BoxHit(node.Min, node.Max, o, d, distance)) return;
        if (node.Left != null) { NearestHit(node.Left, triangles, o, d, distance, wanted, targetY, ref best, ref hitNormal); NearestHit(node.Right, triangles, o, d, distance, wanted, targetY, ref best, ref hitNormal); return; }
        for (int i = node.Start; i < node.Start + node.Count; i++)
            if (targetY >= triangles[i].MinimumTargetY && TriangleRay(triangles[i], o, d, distance, out float t)
                && (float.IsNaN(best) || Math.Abs(t - wanted) < Math.Abs(best - wanted))) { best = t; hitNormal = Vector3.Cross(triangles[i].B - triangles[i].A, triangles[i].C - triangles[i].A); }
    }

    private static bool TriangleRay(Triangle tri, Vector3 o, Vector3 d, float distance, out float t)
    {
        t = 0;
        var e1 = tri.B - tri.A; var e2 = tri.C - tri.A;
        var h = Vector3.Cross(d, e2); float determinant = Vector3.Dot(e1, h);
        if (Math.Abs(determinant) < 1e-9f) return false;
        float inv = 1 / determinant;
        var s = o - tri.A; float u = inv * Vector3.Dot(s, h);
        if (u < -1e-5f || u > 1 + 1e-5f) return false;
        var q = Vector3.Cross(s, e1); float v = inv * Vector3.Dot(d, q);
        if (v < -1e-5f || u + v > 1 + 1e-5f) return false;
        t = inv * Vector3.Dot(e2, q);
        return t >= 0 && t <= distance && (tri.Opacity == null || tri.Opacity.Covers(tri.UVA * (1 - u - v) + tri.UVB * u + tri.UVC * v));
    }

    private static bool BoxHit(Vector3 min, Vector3 max, Vector3 o, Vector3 d, float distance)
    {
        float near = 0, far = distance;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float direction = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float lo = (axis == 0 ? min.X : axis == 1 ? min.Y : min.Z) - 1e-6f;
            float hi = (axis == 0 ? max.X : axis == 1 ? max.Y : max.Z) + 1e-6f;
            if (Math.Abs(direction) < 1e-8f) { if (origin < lo || origin > hi) return false; continue; }
            float a = (lo - origin) / direction, b = (hi - origin) / direction;
            if (a > b) (a, b) = (b, a);
            near = Math.Max(near, a); far = Math.Min(far, b);
            if (near > far) return false;
        }
        return true;
    }

    private static bool TriangleHit(Triangle tri, Vector3 o, Vector3 d, float distance)
    {
        var e1 = tri.B - tri.A; var e2 = tri.C - tri.A;
        var h = Vector3.Cross(d, e2); float determinant = Vector3.Dot(e1, h);
        if (Math.Abs(determinant) < 1e-9f) return false;
        float inv = 1 / determinant;
        var s = o - tri.A; float u = inv * Vector3.Dot(s, h);
        if (u < -1e-5f || u > 1 + 1e-5f) return false;
        var q = Vector3.Cross(s, e1); float v = inv * Vector3.Dot(d, q);
        if (v < -1e-5f || u + v > 1 + 1e-5f) return false;
        float t = inv * Vector3.Dot(e2, q);
        return t >= 0 && t <= distance && (tri.Opacity == null || tri.Opacity.Covers(tri.UVA * (1 - u - v) + tri.UVB * u + tri.UVC * v));
    }
}
