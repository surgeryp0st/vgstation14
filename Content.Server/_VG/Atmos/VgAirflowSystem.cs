using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Shared._VG.Armor;
using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Slip;
using Content.Shared._VG.Status;
using Content.Shared.Buckle;
using Content.Shared.Clothing;
using Content.Shared.Damage;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VG.Atmos;

/// <summary>
///     Remembers when airflow last moved or knocked this over, and how fast it's flying (vg13 last_airflow,
///     last_airflow_stun and airflow_speed).
/// </summary>
[RegisterComponent]
public sealed partial class VgAirflowComponent : Component
{
    [ViewVariables]
    public TimeSpan LastAirflow;

    [ViewVariables]
    public TimeSpan LastStun;

    /// <summary>
    ///     Above 0 while flying from airflow; what it hits takes it into account.
    /// </summary>
    [ViewVariables]
    public float Speed;

    /// <summary>
    ///     The biggest pressure difference it has felt, kPa (for debugging).
    /// </summary>
    [ViewVariables]
    public float MaxDifferential;
}

/// <summary>
///     vg13 ZAS airflow (code/modules/ZAS/Airflow.dm, ConnectionGroup.dm flow()) in place of SS14 space wind's push:
///     a pressure difference knocks people over and sends loose things flying toward the low side, by vg13's
///     thresholds; people slamming into things get hurt. Called from a // VG hook in
///     AtmosphereSystem.ExperiencePressureDifference, with the tile's pressure difference as vg13's differential.
/// </summary>
public sealed class VgAirflowSystem : EntitySystem
{
    [Dependency] private readonly Content.Server.Atmos.EntitySystems.AtmosphereSystem _atmos = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSlipSystem _slip = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    // vg13 ZAS.txt, kPa.
    public const float LightestPressure = 20;
    public const float LightPressure = 35;
    public const float MediumPressure = 50;
    public const float HeavyPressure = 65;
    public const float DensePressure = 85;
    public const float StunPressure = 60;
    private static readonly TimeSpan StunCooldown = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);
    private const float SpeedDecay = 1.5f;
    private const float AirflowDamage = 2;
    private const float AirflowStun = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgAirflowComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<VgAirflowComponent, LandEvent>(OnLand);
    }

    /// <summary>
    ///     vg13 flow() for one thing: knock it over, and send it flying if the difference is enough for its size.
    /// </summary>
    public void Experience(EntityUid uid, float tileDifference, Vector2i step, Vector2 direction, EntityCoordinates target, PhysicsComponent physics)
    {
        var differential = ZoneDifferential(uid, step, tileDifference);
        if (differential < LightestPressure)
            return;

        var airflow = EnsureComp<VgAirflowComponent>(uid);
        airflow.MaxDifferential = MathF.Max(airflow.MaxDifferential, differential);
        var now = _timing.CurTime;
        var isMob = HasComp<MobStateComponent>(uid);

        if (isMob && differential > StunPressure)
            Stun((uid, airflow), differential);

        if (!Movable(uid, differential, isMob, physics) || airflow.LastAirflow != TimeSpan.Zero && now < airflow.LastAirflow + Delay)
            return;

        // vg13 GotoAirflowDest(differential / 10): fly toward the low side, faster the closer it is.
        var from = _transform.GetWorldPosition(uid);
        var to = target.IsValid(EntityManager) ? _transform.ToMapCoordinates(target).Position : from + direction * 2;
        var distance = (to - from).Length();
        var falloff = 9 - distance;
        if (falloff < 1)
            return;

        var speed = Math.Clamp(differential / 10 * (9 / falloff), 1, 9);
        var flight = (to - from).LengthSquared() > 0.01f ? (to - from).Normalized() : direction;
        if (flight == Vector2.Zero)
            return;

        var tiles = MathF.Ceiling(speed / SpeedDecay);
        // vg13 steps every max(1, 10 - (speed + 3)) deciseconds, or every tick above speed 7.
        var throwSpeed = speed > 7 ? 20 : 10 / MathF.Max(1, 10 - (speed + 3));

        airflow.LastAirflow = now;
        airflow.Speed = speed;
        if (isMob)
            _popup.Self(uid, Loc.GetString("vg-airflow-sucked"), null);

        _throwing.TryThrow(uid, flight * tiles, throwSpeed, pushbackRatio: 0, recoil: false, animated: false,
            playSound: false, doSpin: !isMob);
    }

    /// <summary>
    ///     vg13 compares whole zones: everything in a breached room feels the room's pressure against space. SS14 keeps
    ///     a difference per tile, so this follows the flow up to 8 tiles (vg13's view) for the lowest pressure there
    ///     (space counts as none) and takes it from the pressure here.
    /// </summary>
    private float ZoneDifferential(EntityUid uid, Vector2i step, float tileDifference)
    {
        var xform = Transform(uid);
        if (step == Vector2i.Zero || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return tileDifference;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        if (_atmos.GetTileMixture(grid, xform.MapUid, tile) is not { } here)
            return tileDifference;

        var lowest = here.Pressure;
        for (var i = 0; i < 8; i++)
        {
            tile += step;

            // A wall ends the zone; off the grid is the map's air (or space).
            if (_atmos.GetTileMixture(grid, xform.MapUid, tile) is not { } air)
                break;

            var pressure = air.Pressure;
            lowest = MathF.Min(lowest, pressure);
            if (pressure <= 0)
                break;
        }

        return MathF.Max(tileDifference, here.Pressure - lowest);
    }

    /// <summary>
    ///     vg13 airflow_stun(): a big enough rush of air knocks you over, unless your shoes (or walking) keep you up.
    /// </summary>
    private void Stun(Entity<VgAirflowComponent> ent, float differential)
    {
        var now = _timing.CurTime;
        if (ent.Comp.LastStun != TimeSpan.Zero && now < ent.Comp.LastStun + StunCooldown || _mobState.IsDead(ent) || _buckle.IsBuckled(ent))
            return;

        if (_inventory.TryGetSlotEntity(ent, "shoes", out _) && !_slip.CouldSlip(ent))
            return;

        if (!HasComp<Content.Shared.Stunnable.KnockedDownComponent>(ent))
            _popup.Self(ent, Loc.GetString("vg-airflow-knocked-over"), null);

        _status.Knockdown(ent, MathF.Min(_random.NextFloat(differential / 20, differential / 10), 5));
        ent.Comp.LastStun = now;
    }

    /// <summary>
    ///     vg13 check_airflow_movable(): what moves at which difference.
    /// </summary>
    private bool Movable(EntityUid uid, float differential, bool isMob, PhysicsComponent physics)
    {
        if (physics.BodyType == BodyType.Static || Transform(uid).Anchored)
            return false;

        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
            return false;

        if (isMob)
        {
            if (differential < HeavyPressure || _buckle.IsBuckled(uid) || HasComp<VgGrabbedComponent>(uid))
                return false;

            // Switched-on magboots hold you down.
            return !(_inventory.TryGetSlotEntity(uid, "shoes", out var shoes)
                     && HasComp<MagbootsComponent>(shoes)
                     && _toggle.IsActivated(shoes.Value));
        }

        if (TryComp<ItemComponent>(uid, out var item))
        {
            return item.Size.Id switch
            {
                "Tiny" => true,
                "Small" => differential >= LightestPressure,
                "Normal" => differential >= LightPressure,
                _ => differential >= MediumPressure,
            };
        }

        return differential >= DensePressure;
    }

    /// <summary>
    ///     vg13 airflow_hit(): slamming into something stops you, knocks you down and, for people, hurts head, chest and
    ///     groin (less through melee armor) and stuns.
    /// </summary>
    private void OnHit(Entity<VgAirflowComponent> ent, ref ThrowDoHitEvent args)
    {
        var speed = ent.Comp.Speed;
        if (speed <= 0)
            return;

        ent.Comp.Speed = 0;
        if (!HasComp<MobStateComponent>(ent))
            return;

        _popup.Visible(ent,
            Loc.GetString("vg-airflow-slam", ("target", Identity.Entity(ent, EntityManager)), ("thing", args.Target)),
            Loc.GetString("vg-airflow-slam", ("target", Identity.Entity(ent, EntityManager)), ("thing", args.Target)),
            null);

        _status.Knockdown(ent, HasComp<ItemComponent>(args.Target) ? 1 : _random.Next(1, 6));

        if (!HasComp<VgBodyComponent>(ent))
            return;

        var loss = speed * AirflowDamage / 3;
        foreach (var zone in new[] { VgLimbs.Head, VgLimbs.Chest, VgLimbs.Groin })
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = loss;
            _damageable.TryChangeDamage(ent, damage.WithVgAttack(VgArmorType.Melee, zone));
        }

        _status.Stun(ent, MathF.Round(speed * AirflowStun / 2));
    }

    private void OnLand(Entity<VgAirflowComponent> ent, ref LandEvent args)
    {
        ent.Comp.Speed = 0;
    }
}
