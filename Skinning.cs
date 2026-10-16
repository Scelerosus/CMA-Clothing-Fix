using System.Numerics;

namespace CMAClothingFix;

// Pure managed replacement for SkinnedMeshRenderer.BakeMesh: blend shapes, then linear blend skinning with up to 4 influences.
internal static class Skinning
{
    // Sparse blend shape: per frame the frame weight and the non-zero position deltas.
    internal sealed record ShapeFrame(float Weight, int[] Index, Vector3[] Delta);
    internal sealed record Shape(int ShapeIndex, ShapeFrame[] Frames);
    // Index/Weight hold 4 bone influences per vertex, or, when Start is given, Start[v]..Start[v+1] influences per vertex (sorted by weight, descending).
    internal sealed record MeshData(Vector3[] Rest, Vector3[] Normals, int[] Index, float[] Weight, Shape[] Shapes, int[] Start = null);

    // Bones are 3x4 row-major world-from-rest matrices: (bone.localToWorld * bindpose), 12 floats each.
    // maxInfluences mirrors the renderer quality / QualitySettings.skinWeights (the first slots are the largest weights);
    // root is the renderer's own 3x4 matrix, used for vertices that have no usable bone so every output stays in world space.
    internal static (Vector3[] positions, Vector3[] normals) Skin(MeshData mesh, Func<int, float> shapeWeight, float[][] bones, int maxInfluences = 4, float[] root = null)
    {
        root ??= new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
        int n = mesh.Rest.Length;
        var shaped = (Vector3[])mesh.Rest.Clone();
        foreach (var shape in mesh.Shapes)
        {
            float w = shapeWeight(shape.ShapeIndex);
            if (w == 0 || shape.Frames.Length == 0) continue;
            ApplyShape(shaped, shape.Frames, w);
        }
        var positions = new Vector3[n]; var normals = new Vector3[n];
        for (int v = 0; v < n; v++)
        {
            Vector3 p = default, nrm = default; float total = 0;
            int begin = mesh.Start == null ? v * 4 : mesh.Start[v], count = mesh.Start == null ? 4 : mesh.Start[v + 1] - mesh.Start[v];
            for (int k = 0; k < Math.Min(count, Math.Max(maxInfluences, 1)); k++)
            {
                float w = mesh.Weight[begin + k]; int b = mesh.Index[begin + k];
                if (w <= 0 || b < 0 || b >= bones.Length || bones[b] == null) continue;
                var m = bones[b]; var s = shaped[v]; var r = mesh.Normals.Length == n ? mesh.Normals[v] : Vector3.Zero;
                p += w * new Vector3(m[0] * s.X + m[1] * s.Y + m[2] * s.Z + m[3], m[4] * s.X + m[5] * s.Y + m[6] * s.Z + m[7], m[8] * s.X + m[9] * s.Y + m[10] * s.Z + m[11]);
                nrm += w * new Vector3(m[0] * r.X + m[1] * r.Y + m[2] * r.Z, m[4] * r.X + m[5] * r.Y + m[6] * r.Z, m[8] * r.X + m[9] * r.Y + m[10] * r.Z);
                total += w;
            }
            // Unity renormalises influences; a vertex with no usable bone keeps its shaped rest position.
            if (total <= 0)
            {
                var s0 = shaped[v]; var r0 = mesh.Normals.Length == n ? mesh.Normals[v] : Vector3.Zero;
                positions[v] = new Vector3(root[0] * s0.X + root[1] * s0.Y + root[2] * s0.Z + root[3], root[4] * s0.X + root[5] * s0.Y + root[6] * s0.Z + root[7], root[8] * s0.X + root[9] * s0.Y + root[10] * s0.Z + root[11]);
                normals[v] = new Vector3(root[0] * r0.X + root[1] * r0.Y + root[2] * r0.Z, root[4] * r0.X + root[5] * r0.Y + root[6] * r0.Z, root[8] * r0.X + root[9] * r0.Y + root[10] * r0.Z);
                continue;
            }
            positions[v] = p / total;
            float len = nrm.Length();
            normals[v] = len > 1e-9f ? nrm / len : Vector3.Zero;
        }
        return (positions, normals);
    }

    // Unity blend shape weights are 0..100 per frame weight; one frame scales linearly (also beyond 100), several frames interpolate.
    private static void ApplyShape(Vector3[] shaped, ShapeFrame[] frames, float weight)
    {
        if (frames.Length == 1) { Add(shaped, frames[0], weight / frames[0].Weight); return; }
        if (weight <= frames[0].Weight) { Add(shaped, frames[0], weight / frames[0].Weight); return; }
        for (int i = 1; i < frames.Length; i++)
        {
            if (weight > frames[i].Weight && i < frames.Length - 1) continue;
            // Beyond the last frame Unity keeps extrapolating along the last two frames.
            float t = (weight - frames[i - 1].Weight) / (frames[i].Weight - frames[i - 1].Weight);
            t = i == frames.Length - 1 ? Math.Max(t, 0) : Math.Clamp(t, 0, 1);
            Add(shaped, frames[i - 1], 1 - t); Add(shaped, frames[i], t);
            return;
        }
    }

    private static void Add(Vector3[] target, ShapeFrame frame, float scale)
    {
        if (!float.IsFinite(scale)) return;
        for (int i = 0; i < frame.Index.Length; i++) target[frame.Index[i]] += frame.Delta[i] * scale;
    }
}
