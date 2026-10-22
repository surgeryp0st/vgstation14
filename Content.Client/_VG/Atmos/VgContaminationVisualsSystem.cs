using Content.Shared._VG.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._VG.Atmos;

/// <summary>
///     vg13 contamination_overlay: contaminated things get a green tinge over their sprite.
/// </summary>
public sealed class VgContaminationVisualsSystem : EntitySystem
{
    private const string LayerKey = "vg-contamination";
    private static readonly SpriteSpecifier Overlay = new SpriteSpecifier.Rsi(new ResPath("/Textures/_VG/effects/contamination.rsi"), "contaminated");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgContaminatedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VgContaminatedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<VgContaminatedComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) || sprite.LayerMapTryGet(LayerKey, out _))
            return;

        var layer = sprite.AddLayer(Overlay);
        sprite.LayerMapSet(LayerKey, layer);
    }

    private void OnShutdown(Entity<VgContaminatedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.LayerMapTryGet(LayerKey, out var layer))
            sprite.RemoveLayer(layer);
    }
}
