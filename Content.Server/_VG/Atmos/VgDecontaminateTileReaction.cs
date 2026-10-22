using Content.Shared._VG.Atmos;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server._VG.Atmos;

/// <summary>
///     vg13 bleach reaction_turf(): things lying on the tile are washed clean of plasma.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class VgDecontaminateTileReaction : ITileReaction
{
    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero)
            return FixedPoint2.Zero;

        var lookup = entityManager.System<EntityLookupSystem>();
        foreach (var uid in lookup.GetLocalEntitiesIntersecting(tile.GridUid, tile.GridIndices, 0f))
        {
            entityManager.RemoveComponent<VgContaminatedComponent>(uid);
        }

        return FixedPoint2.Zero;
    }
}
