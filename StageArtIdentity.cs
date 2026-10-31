#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SilverpineMods.CustomGrowables;

// Independent of Unity so cache ownership and placement math can be regression tested.
internal static class StageArtIdentity
{
    internal static string Suffix(string? id, int legacyIndex, string? model, float[]? rotation, float zoom, int resolution)
    {
        if (string.IsNullOrEmpty(id)) return "__growable_stage_" + legacyIndex;
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write((model ?? "").Trim().Replace('\\', '/'));
            writer.Write(rotation is { Length: 3 } ? rotation[0] : 20f);
            writer.Write(rotation is { Length: 3 } ? rotation[1] : 135f);
            writer.Write(rotation is { Length: 3 } ? rotation[2] : 0f);
            writer.Write(zoom); writer.Write(resolution);
        }
        using var sha = SHA256.Create();
        return "__growable_stage_" + id + "_" + BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "").ToLowerInvariant();
    }

    internal static float ShiftPivot(float pivotPixels, float size, float ppu, float scale, float offset)
    {
        double pivot = (pivotPixels - (double)offset * ppu / scale) / size;
        if (double.IsNaN(pivot) || double.IsInfinity(pivot) || Math.Abs(pivot) > float.MaxValue)
            throw new InvalidDataException("Stage geometry exceeds Unity's supported numeric range.");
        return (float)pivot;
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static void ValidateOffset(float x, float y)
    {
        if (!Finite(x) || !Finite(y) || Math.Abs(x) > 100 || Math.Abs(y) > 100)
            throw new InvalidDataException("Stage offsets must be finite and between -100 and 100 tiles.");
    }
}
