using UnityEngine;
using Object = UnityEngine.Object;

namespace CMAClothingFix;

internal sealed class AlphaCoverage
{
    private const int Size = 256;
    private sealed record Entry(uint Revision, CoverageGeometry.AlphaImage Image);
    private readonly Dictionary<int, Entry> images = new();
    private readonly HashSet<int> wanted = new(), warned = new();
    private int readsLeft;
    internal int SampledTextures => images.Values.Count(x => x.Image != null);

    internal void Begin() { wanted.Clear(); readsLeft = 1; }
    internal void End()
    {
        foreach (int id in images.Keys.Where(id => !wanted.Contains(id)).ToArray()) { images.Remove(id); warned.Remove(id); }
    }
    internal void Clear() { images.Clear(); wanted.Clear(); warned.Clear(); }

    internal CoverageGeometry.AlphaImage Read(Texture source)
    {
        int id = source.GetInstanceID();
        wanted.Add(id);
        uint revision = source.updateCount;
        if (images.TryGetValue(id, out var entry) && entry.Revision == revision) return entry.Image;
        if (readsLeft == 0) return null;
        readsLeft--;
        try
        {
            var texture = source.TryCast<Texture2D>();
            if (texture == null) { images[id] = new Entry(revision, null); return null; }
            int width = Math.Min(Size, texture.width), height = Math.Min(Size, texture.height);
            if (width <= 0 || height <= 0) { images[id] = new Entry(revision, null); return null; }
            var previous = RenderTexture.active;
            RenderTexture temporary = null;
            Texture2D copy = null;
            try
            {
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> pixels = null;
                if (texture.isReadable && texture.width <= Size && texture.height <= Size)
                    try { pixels = texture.GetPixels32(); } catch { /* Compressed readable formats can still require a GPU copy. */ }
                if (pixels == null)
                {
                    temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(texture, temporary);
                    RenderTexture.active = temporary;
                    copy = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                    copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    pixels = copy.GetPixels32();
                }
                if (pixels.Length != width * height) throw new InvalidOperationException("Texture alpha size differs from its image.");
                var alpha = pixels.Select(pixel => pixel.a).ToArray();
                var image = new CoverageGeometry.AlphaImage(width, height, alpha, revision);
                images[id] = new Entry(revision, image);
                return image;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (copy != null) Object.Destroy(copy);
            }
        }
        catch (Exception ex)
        {
            images[id] = new Entry(revision, null);
            if (warned.Add(id)) Plugin.Logger.LogWarning($"Alpha sampling for '{source.name}' skipped: {ex.Message}");
            return null;
        }
    }
}
