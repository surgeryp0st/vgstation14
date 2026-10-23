using System.Numerics;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Status;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Fluids.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Slippery;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Slip;

/// <summary>
///     vg13 slipping (carbon ApplySlip(), CheckSlip() and Slip()) for vg mobs, called from a // VG hook at the top of
///     SS14's SlipperySystem.TrySlip. Predicted, as SS14's slipping is.
/// </summary>
public sealed class VgSlipSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private const string Lube = "SpaceLube";
    private const int SaltSlip = 50;

    /// <summary>
    ///     How fast a slide carries you, tiles a second (vg13 lube: four more tiles a decisecond apart).
    /// </summary>
    private const float SlideSpeed = 10;

    /// <summary>
    ///     Handles a slip for vg mobs. Returns false to leave everything else to SS14's slipping.
    /// </summary>
    public bool TrySlip(EntityUid source, SlipperyComponent slippery, EntityUid victim)
    {
        if (!HasComp<VgStatusComponent>(victim))
            return false;

        var (kind, stun, knockdown, what) = Describe(source);

        // vg13 CheckSlip(): on your feet, not strapped in, and running unless it slips walkers too.
        if (_standing.IsDown(victim) || _buckle.IsBuckled(victim))
            return true;

        var slipsWalkers = kind is VgSlipKind.Lube or VgSlipKind.Item;
        if (!slipsWalkers && TryComp<InputMoverComponent>(victim, out var mover) && !mover.Sprinting)
            return true;

        // A thrown banana peel doesn't slip anyone.
        var causing = new SlipCausingAttemptEvent();
        RaiseLocalEvent(source, ref causing);
        if (causing.Cancelled)
            return true;

        // Shoes that don't slip (and magboots) help with everything but lube.
        if (kind != VgSlipKind.Lube)
        {
            var attempt = new SlipAttemptEvent();
            RaiseLocalEvent(victim, attempt);
            if (attempt.NoSlip)
                return true;
        }

        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(victim).Id, SaltSlip);
        if (kind == VgSlipKind.Ice && (HasComp<ThrownItemComponent>(victim) || !random.Prob(15)))
            return true;

        Slip(victim, source, kind, stun, knockdown, what, slippery);
        return true;
    }

    /// <summary>
    ///     What kind of slip a slippery thing is, with vg13's stun and knockdown and what you slipped on.
    /// </summary>
    private (VgSlipKind Kind, float Stun, float Knockdown, string What) Describe(EntityUid source)
    {
        if (TryComp<VgSlipComponent>(source, out var slip))
        {
            return slip.Kind switch
            {
                VgSlipKind.Ice => (VgSlipKind.Ice, 4, 3, Loc.GetString("vg-slip-icy-floor")),
                VgSlipKind.Lube => (VgSlipKind.Lube, 5, 3, Loc.GetString("vg-slip-floor")),
                VgSlipKind.Water => (VgSlipKind.Water, 5, 3, Loc.GetString("vg-slip-wet-floor")),
                _ => (VgSlipKind.Item, slip.Stun, slip.Knockdown, Loc.GetString("vg-slip-thing", ("thing", source))),
            };
        }

        if (TryComp<PuddleComponent>(source, out var puddle))
        {
            var lube = _solutions.TryGetSolution(source, puddle.SolutionName, out _, out var solution)
                       && solution.GetTotalPrototypeQuantity(Lube) > 0;
            return lube
                ? (VgSlipKind.Lube, 5, 3, Loc.GetString("vg-slip-floor"))
                : (VgSlipKind.Water, 5, 3, Loc.GetString("vg-slip-wet-floor"));
        }

        // Anything else slippery slips like vg13's banana peel.
        return (VgSlipKind.Item, 2, 2, Loc.GetString("vg-slip-thing", ("thing", source)));
    }

    /// <summary>
    ///     vg13 Slip() and what each kind of slip does after.
    /// </summary>
    private void Slip(EntityUid victim, EntityUid source, VgSlipKind kind, float stun, float knockdown, string what, SlipperyComponent slippery)
    {
        var direction = Direction(victim);

        // What SS14's slipping tells the slippery thing, for whatever listens.
        var slipped = new SlipEvent(victim);
        RaiseLocalEvent(source, ref slipped);

        if (TryComp<PullerComponent>(victim, out var puller) && puller.Pulling is { } pulled && TryComp<PullableComponent>(pulled, out var pullable))
            _pulling.TryStopPull(pulled, pullable, victim);

        _status.Stun(victim, stun);
        _status.Knockdown(victim, knockdown);

        _popup.Visible(victim,
            Loc.GetString("vg-slip-others", ("target", Identity.Entity(victim, EntityManager)), ("what", what)),
            Loc.GetString("vg-slip-self", ("what", what)),
            victim);
        _audio.PlayPredicted(slippery.SlipSound, victim, victim);

        var tiles = kind switch
        {
            VgSlipKind.Water => 1,
            // A step, then four more.
            VgSlipKind.Lube => 5,
            // Until you're off the ice.
            VgSlipKind.Ice => IceAhead(victim, direction) + 1,
            _ => 0,
        };

        if (kind == VgSlipKind.Lube)
        {
            var bruise = new DamageSpecifier();
            bruise.DamageDict["Blunt"] = 2;
            _damageable.TryChangeDamage(victim, bruise, origin: source);
        }

        if (tiles > 0 && direction != Vector2.Zero)
        {
            _throwing.TryThrow(victim, direction * tiles, SlideSpeed, pushbackRatio: 0, recoil: false,
                animated: false, playSound: false, doSpin: false);
        }

        _adminLog.Add(LogType.Slip, LogImpact.Low, $"{ToPrettyString(victim):mob} slipped on {ToPrettyString(source):entity} ({kind})");
    }

    /// <summary>
    ///     The way the mob is going (or facing, if it's standing still), in world space.
    /// </summary>
    private Vector2 Direction(EntityUid uid)
    {
        if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(uid, out var physics) && physics.LinearVelocity.LengthSquared() > 0.01f)
            return physics.LinearVelocity.Normalized();

        return _transform.GetWorldRotation(uid).ToWorldVec();
    }

    /// <summary>
    ///     How many tiles of ice there are in a row ahead.
    /// </summary>
    private int IceAhead(EntityUid uid, Vector2 direction)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp) || direction == Vector2.Zero)
            return 0;

        var step = new Vector2i((int) MathF.Round(direction.X), (int) MathF.Round(direction.Y));
        if (step == Vector2i.Zero)
            return 0;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var count = 0;
        for (var i = 0; i < 64; i++)
        {
            tile += step;
            var ice = false;
            foreach (var ent in _map.GetAnchoredEntities(grid, gridComp, tile))
            {
                if (TryComp<VgSlipComponent>(ent, out var slip) && slip.Kind == VgSlipKind.Ice)
                {
                    ice = true;
                    break;
                }
            }

            if (!ice)
                break;
            count++;
        }

        return count;
    }
}
