#nullable enable
using System;

namespace SilverpineMods.CustomGrowables;

// Value snapshots avoid serializing JSON on every IMGUI Layout/Repaint event.
// Copy rotation values so an in-place edit also invalidates the preview.
internal readonly struct StagePreviewSource : IEquatable<StagePreviewSource>
{
    private readonly (string? item, string? id, string? sprite, string? image, string? model,
        float x, float y, float z, float zoom, int resolution) state;

    internal StagePreviewSource(string? item, GrowableStageDefinition stage)
    {
        bool rotated = stage.rotation is { Length: 3 };
        state = (item, stage.cacheId, stage.sprite, stage.image, stage.model,
            rotated ? stage.rotation![0] : 20f, rotated ? stage.rotation![1] : 135f,
            rotated ? stage.rotation![2] : 0f, stage.zoom, stage.resolution);
    }

    public bool Equals(StagePreviewSource other) => state.Equals(other.state);
}

internal readonly struct StagePreviewGeometry : IEquatable<StagePreviewGeometry>
{
    private readonly (float baseScale, float scale, float ppu, float pivotX, float pivotY,
        string? anchor, float offsetX, float offsetY) state;

    internal StagePreviewGeometry(float baseScale, GrowableStageDefinition stage) =>
        state = (baseScale, stage.scale, stage.pixelsPerUnit, stage.pivotX, stage.pivotY,
            stage.anchorSprite, stage.offsetX, stage.offsetY);

    public bool Equals(StagePreviewGeometry other) => state.Equals(other.state);
}
