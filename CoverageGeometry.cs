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
    internal sealed record Result(int[][] Kept, int Removed, int Total, NewVertex[] Added = null);

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

    internal static Result Mask(Surface target, Vector3[] normals, Surface cover, int[] targetSubmeshes, CancellationToken cancel, float edgeInset = 0, bool bodyJewellery = false, bool refine = false, bool skin = false, float reach = .055f, float depth = .035f, float scale = 1, float keepBelowY = float.NegativeInfinity, bool[] keepVertex = null, bool[] toeVertex = null)
    {
        cancel.ThrowIfCancellationRequested();
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
        if (triangles.Length == 0) return new Result(target.Triangles, 0, 0);
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
                        && Sample((a + b + c * 2) / 4, na + nb + nc * 2);
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
        return new Result(kept, removed, total, added.ToArray());
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
