using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Slip;
using Content.Shared.Atmos;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VG.Atmos;

/// <summary>
///     vg13 zone check_for_events() and ice process(): cryotheum in freezing air ices the floor over, and ice melts once
///     it's warmer than 0 C.
/// </summary>
public sealed class VgIceSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private const string IcePrototype = "VgIce";

    /// <summary>
    ///     vg13 MOLES_CRYOTHEUM_VISIBLE.
    /// </summary>
    private const float MolesCryotheumVisible = 0.7f;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;
        _next = now + Interval;

        Melt();
        Freeze();
    }

    private void Melt()
    {
        var query = EntityQueryEnumerator<VgIceComponent, TransformComponent>();
        var melted = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var ice, out var xform))
        {
            if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (_atmos.GetTileMixture(grid, null, tile) is not { } air)
                continue;

            if (Thaw(ice, air.Temperature))
                melted.Add(uid);
        }

        foreach (var uid in melted)
        {
            QueueDel(uid);
        }
    }

    /// <summary>
    ///     vg13 ice process(): thicker by 1 below 0 C; above, thinner by log8 of how much warmer over 1 to 3. True once
    ///     it has melted away.
    /// </summary>
    public bool Thaw(VgIceComponent ice, float temperature)
    {
        var delta = temperature - Atmospherics.T0C;
        if (delta != 0)
            ice.Thickness = MathF.Min(100, ice.Thickness + (delta < 0 ? 1 : -MathF.Log(MathF.Abs(delta), 8) / _random.Next(1, 4)));

        return ice.Thickness < 0;
    }

    private void Freeze()
    {
        var grids = EntityQueryEnumerator<MapGridComponent>();
        while (grids.MoveNext(out var grid, out var gridComp))
        {
            foreach (var tileRef in _map.GetAllTiles(grid, gridComp))
            {
                if (_atmos.GetTileMixture(grid, null, tileRef.GridIndices) is not { } air
                    || air.Temperature > Atmospherics.T0C
                    || air.Volume <= 0
                    || air.GetMoles(Gas.Cryotheum) / air.Volume * Atmospherics.CellVolume <= MolesCryotheumVisible)
                {
                    continue;
                }

                var iced = false;
                foreach (var ent in _map.GetAnchoredEntities(grid, gridComp, tileRef.GridIndices))
                {
                    if (HasComp<VgIceComponent>(ent))
                    {
                        iced = true;
                        break;
                    }
                }

                if (!iced)
                    Spawn(IcePrototype, _map.ToCenterCoordinates(grid, tileRef.GridIndices, gridComp));
            }
        }
    }
}
