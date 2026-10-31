#nullable enable
using System;
using System.IO;
using System.Linq;
using SilverpineMods.CustomItemLoader;
namespace SilverpineMods.CustomGrowables;
/// <summary>CIL-specific rendered-model cache migration for add-ons; not the shared art library.</summary>
internal static class StageCacheMigration
{
    /// <summary>Reads both legacy and current CIL cache names without changing the cache.</summary>
    public static string GetReadableGlbCachePath(string folder, string packId, string itemId, string suffix) =>
        GlbCacheApi.GetReadableGlbCachePath(folder, packId, itemId, suffix);

    /// <summary>Preserves an old stage cache under a new immutable identity; leaves originals intact.</summary>
    public static void CopyGlbCache(string folder, string packId, string itemId, string oldSuffix, string newSuffix)
    {
        string source = GetReadableGlbCachePath(folder, packId, itemId, oldSuffix);
        if (!File.Exists(source)) return;
        string destination = CustomItemApi.GetGlbSpriteCachePath(folder, packId, itemId, newSuffix);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // Never replace an unrelated or already published cache.
        if (File.Exists(destination))
        {
            if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination)))
                throw new IOException("Stage cache identity collision.");
        }
        string key = Path.ChangeExtension(source, ".key");
        string destinationKey = Path.ChangeExtension(destination, ".key");
        if (File.Exists(key)) CopyImmutable(key, destinationKey);
        CopyImmutable(source, destination);
    }

    private static void CopyImmutable(string source, string destination)
    {
        byte[] bytes = File.ReadAllBytes(source);
        if (File.Exists(destination))
        {
            if (!bytes.SequenceEqual(File.ReadAllBytes(destination)))
                throw new IOException("Stage cache identity collision.");
            return;
        }
        // Never publish a partial copy. A completed orphan key is accepted on retry.
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
