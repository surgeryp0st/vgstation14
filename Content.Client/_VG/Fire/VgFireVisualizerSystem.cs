using Content.Shared._VG.Fire;
using Robust.Client.GameObjects;

namespace Content.Client._VG.Fire;

/// <summary>
///     Picks the flame sprite (vg13 fire.dmi key1 to key3) from how big the fire is.
/// </summary>
public sealed class VgFireVisualizerSystem : VisualizerSystem<VgFireComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, VgFireComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !AppearanceSystem.TryGetData<int>(uid, VgFireVisuals.Size, out var size, args.Component))
            return;

        args.Sprite.LayerSetState(0, $"key{Math.Clamp(size, 1, 3)}");
    }
}
