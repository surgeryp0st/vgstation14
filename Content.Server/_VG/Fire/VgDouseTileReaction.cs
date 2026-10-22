using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server._VG.Fire;

/// <summary>
///     vg13 water on a tile: puts out the fire and whatever is burning there (<see cref="VgFireSystem.Douse"/>).
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class VgDouseTileReaction : ITileReaction
{
    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
            return FixedPoint2.Zero;

        entityManager.System<VgFireSystem>().Douse(tile.GridUid, tile.GridIndices);
        return FixedPoint2.Zero;
    }
}
