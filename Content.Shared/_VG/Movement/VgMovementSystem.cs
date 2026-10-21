using System.Linq;
using Content.Shared._VG.Body;
using Content.Shared._VG.Clothing;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Life;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Status;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Movement;

/// <summary>
///     vg13 movement speed. vg13 moves a mob one tile per movement_delay() deciseconds: a tally (run 1, walk 4, plus
///     slowdowns) times multipliers (bare feet, worn items...), and the next step waits for the first world tick at or
///     after that. This works the delay out the same way and turns it into SS14 walk/sprint speeds.
/// </summary>
public sealed class VgMovementSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly VgClothingSystem _clothing = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    // vg13 MOB_RUN_TALLY and MOB_WALK_TALLY, in deciseconds per tile.
    private const float RunTally = 1;
    private const float WalkTally = 4;

    /// <summary>
    ///     vg13 config TICKLAG: a step happens on the first world tick at or after the delay.
    /// </summary>
    private const float TickLag = 0.33f;

    /// <summary>
    ///     vg13 move_delayer min_delay.
    /// </summary>
    private const float MinDelay = 0.3f;

    /// <summary>
    ///     vg13 NO_SHOES_SLOWDOWN.
    /// </summary>
    private const float NoShoesSlowdown = 1.4f;

    /// <summary>
    ///     vg13: holding someone in a grab adds 7 to the tally.
    /// </summary>
    private const float GrabTally = 7;

    private static readonly string[] WornSlots =
        { "head", "mask", "eyes", "ears", "neck", "outerClothing", "jumpsuit", "gloves", "shoes", "belt", "back", "id", "suitstorage" };

    private static readonly EntProtoId[] Canes = { "Cane", "WhiteCane" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgMovementComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VgMovementComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<VgMovementComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<VgMovementComponent, DidEquipEvent>((uid, _, _) => _speed.RefreshMovementSpeedModifiers(uid));
        SubscribeLocalEvent<VgMovementComponent, DidUnequipEvent>((uid, _, _) => _speed.RefreshMovementSpeedModifiers(uid));
    }

    /// <summary>
    ///     How fast a vg13 delay moves you, in tiles per second.
    /// </summary>
    public static float SpeedOf(float delay)
    {
        delay = MathF.Max(MinDelay, delay);
        var ticks = MathF.Ceiling(delay / TickLag - 0.0001f);
        return 10 / (ticks * TickLag);
    }

    private void OnMapInit(Entity<VgMovementComponent> ent, ref MapInitEvent args)
    {
        // A plain vg13 human: 7.6 tiles/s running, 2.3 walking.
        var move = EnsureComp<MovementSpeedModifierComponent>(ent);
        _speed.ChangeBaseSpeed(ent, SpeedOf(WalkTally), SpeedOf(RunTally), move.Acceleration, move);
    }

    private void OnLifeTick(Entity<VgMovementComponent> ent, ref VgLifeTickEvent args)
    {
        // Damage, pain and broken limbs change the delay; vg13 works it out every step, we do it every Life() tick.
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefreshSpeed(Entity<VgMovementComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<VgBodyComponent>(ent, out var body))
            return;

        var extra = ExtraTally((ent, body));
        var multiplier = TallyMultiplier((ent, body));

        var walk = SpeedOf((WalkTally + extra) * multiplier) / SpeedOf(WalkTally);
        var run = SpeedOf((RunTally + extra) * multiplier) / SpeedOf(RunTally);
        args.ModifySpeed(walk, run);
    }

    /// <summary>
    ///     vg13 base_movement_tally() on top of run/walk: grabs, damage, traumatic shock and bad legs.
    /// </summary>
    public float ExtraTally(Entity<VgBodyComponent> ent)
    {
        var tally = 0f;

        if (HasComp<VgGrabberComponent>(ent))
            tally += GrabTally;

        // vg13: below 400 nutrition you start to slow down (80 at the default level, 100 when overfed).
        var hungry = (500 - _nutrition.GetNutrition(ent)) / 5;
        if (hungry >= 70)
            tally += hungry / 50;

        // TODO VG: hypothermia, drowsiness and painkillers come with temperature and chemistry.
        // vg13 carbon: being badly hurt slows you down.
        if (TryComp<DamageableComponent>(ent, out var damageable))
        {
            var deficiency = damageable.TotalDamage.Float() - _status.GetHalloss(ent);
            if (deficiency >= 40)
                tally += deficiency / 25;
        }

        if (TryComp<VgStatusComponent>(ent, out var status) && status.ShockStage >= 50)
            tally += 3;

        // Missing, peg, broken or splinted legs; on the floor you crawl on your hands instead.
        var lying = _standing.IsDown(ent);
        var multiplier = lying ? 2 : 1;
        var cane = HoldingCane(ent);
        foreach (var limb in ent.Comp.Limbs.Values)
        {
            var used = lying
                ? limb.CanGrasp
                : (limb.BodyPart & (VgBodyParts.Legs | VgBodyParts.Feet)) != 0;
            if (!used)
                continue;

            if (!limb.IsExisting || limb.IsPeg)
            {
                tally += 2 * multiplier;
                if (cane)
                    tally -= multiplier;
            }

            if ((limb.Status & VgLimbStatus.Splinted) != 0)
            {
                if (!cane)
                    tally += 0.5f * multiplier;
            }
            else if ((limb.Status & VgLimbStatus.Broken) != 0)
            {
                if (!cane)
                    tally += multiplier;
                tally += 0.5f * multiplier;
            }
        }

        return tally;
    }

    /// <summary>
    ///     vg13 movement_tally_multiplier(): bare feet and heavy clothing.
    /// </summary>
    public float TallyMultiplier(Entity<VgBodyComponent> ent)
    {
        var multiplier = 1f;

        if (!_inventory.TryGetSlotEntity(ent, "shoes", out _) && HasVulnerableFoot(ent.Comp))
            multiplier *= NoShoesSlowdown;

        foreach (var slot in WornSlots)
        {
            if (!_inventory.TryGetSlotEntity(ent, slot, out var item) || _clothing.Get(item.Value) is not { } clothing)
                continue;

            // Things like magboots only weigh you down while they're on.
            if (HasComp<Content.Shared.Item.ItemToggle.Components.ItemToggleComponent>(item) && !_toggle.IsActivated(item.Value))
                continue;

            // Heavy things on your back count for half.
            multiplier *= slot == "back" ? MathF.Max(1, clothing.Slowdown / 2) : clothing.Slowdown;
        }

        return multiplier;
    }

    /// <summary>
    ///     vg13 has_vulnerable_foot(): a fleshy foot to step on things with.
    /// </summary>
    private static bool HasVulnerableFoot(VgBodyComponent body)
    {
        foreach (var limb in body.Limbs.Values)
        {
            if ((limb.BodyPart & VgBodyParts.Feet) != 0 && limb.IsExisting && limb.IsOrganic)
                return true;
        }

        return false;
    }

    private bool HoldingCane(EntityUid uid)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (MetaData(held).EntityPrototype is not { } proto)
                continue;

            foreach (var cane in Canes)
            {
                if (proto.ID == cane || _proto.EnumerateParents<EntityPrototype>(proto.ID).Any(p => p.ID == cane))
                    return true;
            }
        }

        return false;
    }
}
