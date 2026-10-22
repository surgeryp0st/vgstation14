using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Fire;
using Content.Shared.Atmos;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Temperature;
using Content.Shared.Tools.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._VG.Fire;

/// <summary>
///     vg13 plasma walls: PlasmaBurn() when hit with something hot or shot with a beam.
/// </summary>
public sealed class VgPlasmaBurnSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // vg13 is_hot() temperatures (__DEFINES/reagents.dm).
    public const float WelderTemperature = 3480; // TEMPERATURE_WELDER
    public const float FlameTemperature = 700; // TEMPERATURE_FLAME

    /// <summary>
    ///     vg13 bullet_act(): a beam sets it off at 2500 K.
    /// </summary>
    public const float BeamTemperature = 2500;

    /// <summary>
    ///     vg13 MEDIUM_FLAME, the volume the burst exposes its tile with.
    /// </summary>
    private const float MediumFlame = 250;

    private static readonly TimeSpan BurstTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     A wall's tile has no air of its own until atmos sees the wall is gone, so the plasma waits for it.
    /// </summary>
    private readonly List<Burst> _bursts = new();

    private record struct Burst(EntityUid Grid, Vector2i Tile, float Plasma, float PlasmaTemperature, float Temperature, EntityUid? User, TimeSpan GiveUp);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgPlasmaBurnComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<VgPlasmaBurnComponent, HitScanReflectAttemptEvent>(OnHitscan);
    }

    private void OnInteractUsing(Entity<VgPlasmaBurnComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // vg13 attackby(): W.is_hot() > 300 ignites it.
        var hot = new IsHotEvent();
        RaiseLocalEvent(args.Used, hot);
        if (!hot.IsHot)
            return;

        var temperature = TryComp<WelderComponent>(args.Used, out var welder) && welder.Enabled ? WelderTemperature : FlameTemperature;
        args.Handled = Ignite(ent, temperature, args.User);
    }

    private void OnHitscan(Entity<VgPlasmaBurnComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        Ignite(ent, BeamTemperature, args.Shooter);
    }

    /// <summary>
    ///     vg13 ignite() and PlasmaBurn(): above its minimum temperature it goes up, leaving its girder and hot plasma,
    ///     and sets off the plasma walls around it at a quarter of the temperature.
    /// </summary>
    public bool Ignite(Entity<VgPlasmaBurnComponent> ent, float temperature, EntityUid? user = null)
    {
        if (ent.Comp.Burning || temperature <= ent.Comp.MinTemperature || TerminatingOrDeleted(ent))
            return false;

        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        ent.Comp.Burning = true;
        var coords = xform.Coordinates;
        var tile = _map.TileIndicesFor(grid, gridComp, coords);

        if (user != null)
            _adminLog.Add(LogType.Flammable, LogImpact.High, $"{ToPrettyString(user.Value):user} set {ToPrettyString(ent):target} alight at {temperature} K");
        else
            _adminLog.Add(LogType.Flammable, LogImpact.High, $"{ToPrettyString(ent):target} caught fire at {temperature} K");

        if (ent.Comp.LeavesBehind is { } leftover)
            Spawn(leftover, _map.ToCenterCoordinates(grid, tile, gridComp));

        _bursts.Add(new Burst(grid, tile, ent.Comp.Plasma, ent.Comp.PlasmaTemperature, temperature, user, _timing.CurTime + BurstTimeout));
        var range = ent.Comp.ChainRange;
        QueueDel(ent);

        // The walls around it (vg13 range(3, src): a square).
        var box = new Box2(tile - new Vector2i(range, range), tile + new Vector2i(range + 1, range + 1));
        var others = new HashSet<Entity<VgPlasmaBurnComponent>>();
        _lookup.GetLocalEntitiesIntersecting(grid, box, others, flags: LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Approximate);
        foreach (var other in others)
        {
            Ignite(other, temperature / 4, user);
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_bursts.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            var air = _atmos.GetTileMixture(burst.Grid, null, burst.Tile, true);
            if (air == null)
            {
                if (now >= burst.GiveUp || TerminatingOrDeleted(burst.Grid))
                    _bursts.RemoveAt(i);
                continue;
            }

            _bursts.RemoveAt(i);

            var napalm = new GasMixture(Atmospherics.CellVolume) { Temperature = burst.PlasmaTemperature };
            napalm.AdjustMoles(Gas.Plasma, burst.Plasma);
            _atmos.Merge(air, napalm);
            _atmos.HotspotExpose(burst.Grid, burst.Tile, burst.Temperature, MediumFlame, burst.User, soh: true);
        }
    }
}
