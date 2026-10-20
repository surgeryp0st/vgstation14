using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Random;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Medical;

/// <summary>
///     vg13 medical stacks: bandaging, salving and splinting the limb the user aims at. Predicted: it runs from the
///     shared interaction code, and the advanced kits' healing roll is seeded per tick.
/// </summary>
public sealed class VgMedicalSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly VgCombatSystem _combat = default!;

    private static readonly ProtoId<DamageGroupPrototype> BruteGroup = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> BurnGroup = "Burn";

    /// <summary>
    ///     vg13: splinting yourself works three times in four.
    /// </summary>
    private const float SelfSplintChance = 75;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgMedicalComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<VgMedicalComponent, VgSplintDoAfterEvent>(OnSplintDoAfter);
    }

    private void OnAfterInteract(Entity<VgMedicalComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<MobStateComponent>(target))
            return;

        // vg13 returns from attack() either way, so the stack never gets swung at them.
        args.Handled = true;

        if (!TryComp<VgBodyComponent>(target, out var body))
        {
            ApplyToSimpleMob(ent, args.User, target);
            return;
        }

        var zone = VgLimbs.CheckZone(_combat.GetZone(args.User));
        if (!_body.TryGetLimbForZone((target, body), zone, out var limb) || !limb.IsExisting)
            return;

        var user = args.User;
        var limbName = Loc.GetString(limb.Name);
        var targetIdentity = Identity.Entity(target, EntityManager);

        // vg13 checks for a space helmet on the head, or a space suit on anything else.
        if (_inventory.TryGetSlotEntity(target, zone == VgLimbs.Head ? "head" : "outerClothing", out var cover) &&
            HasComp<VgSpaceproofComponent>(cover))
        {
            Self(user, Loc.GetString("vg-medical-blocked", ("item", ent.Owner), ("cover", cover)));
            return;
        }

        if (limb.IsRobotic || limb.IsPeg)
        {
            Self(user, Loc.GetString(limb.IsPeg ? "vg-medical-peg" : "vg-medical-robotic"));
            return;
        }

        switch (ent.Comp.Kind)
        {
            case VgMedicalKind.Bandage:
                ApplyBandage(ent, user, (target, body), limb, limbName, targetIdentity);
                break;
            case VgMedicalKind.Salve:
                ApplySalve(ent, user, (target, body), limb, limbName, targetIdentity);
                break;
            case VgMedicalKind.Splint:
                StartSplint(ent, user, target, limb, limbName, targetIdentity);
                break;
        }
    }

    /// <summary>
    ///     vg13 bruise_pack and advanced/bruise_pack attack().
    /// </summary>
    private void ApplyBandage(Entity<VgMedicalComponent> ent, EntityUid user, Entity<VgBodyComponent> body, VgLimb limb, string limbName, EntityUid target)
    {
        if (limb.Open > 0)
        {
            Self(user, Loc.GetString(ent.Comp.Advanced ? "vg-medical-open-advanced-bandage" : "vg-medical-open-bandage",
                ("target", target), ("limb", limbName)));
            return;
        }

        if (!_body.Bandage(body, limb))
        {
            Self(user, Loc.GetString(ent.Comp.Advanced ? "vg-medical-already-treated" : "vg-medical-already-bandaged",
                ("target", target), ("limb", limbName)));
            return;
        }

        var prefix = ent.Comp.Advanced ? "vg-medical-advanced-bandage" : "vg-medical-bandage";
        foreach (var wound in limb.Wounds)
        {
            if (_body.IsInternal(wound))
                continue;

            var kind = _body.IsAtBleedingStage(wound) ? "bleeding"
                : _body.GetWoundType(wound) == VgWoundType.Bruise ? "bruise"
                : "other";

            Visible(user, body, Loc.GetString($"{prefix}-{kind}",
                ("user", Identity.Entity(user, EntityManager)),
                ("target", target),
                ("wound", Loc.GetString(_body.StageDesc(wound))),
                ("limb", limbName)));
        }

        if (ent.Comp.HealBrute > 0)
            _body.HealLimb(body, limb, RollHeal(ent, user, ent.Comp.HealBrute), 0);

        _stack.Use(ent, 1);
    }

    /// <summary>
    ///     vg13 ointment and advanced/ointment attack().
    /// </summary>
    private void ApplySalve(Entity<VgMedicalComponent> ent, EntityUid user, Entity<VgBodyComponent> body, VgLimb limb, string limbName, EntityUid target)
    {
        if (limb.Open > 0)
        {
            Self(user, Loc.GetString(ent.Comp.Advanced ? "vg-medical-open-advanced-salve" : "vg-medical-open-salve",
                ("target", target), ("limb", limbName)));
            return;
        }

        if (!_body.Salve(body, limb))
        {
            Self(user, Loc.GetString("vg-medical-already-salved", ("target", target), ("limb", limbName)));
            return;
        }

        Visible(user, body, Loc.GetString(ent.Comp.Advanced ? "vg-medical-advanced-salve" : "vg-medical-salve",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", target),
            ("limb", limbName)));

        if (ent.Comp.HealBurn > 0)
            _body.HealLimb(body, limb, 0, RollHeal(ent, user, ent.Comp.HealBurn));

        _stack.Use(ent, 1);
    }

    /// <summary>
    ///     vg13 splint attack(): five seconds of wrapping, then the limb is splinted.
    /// </summary>
    private void StartSplint(Entity<VgMedicalComponent> ent, EntityUid user, EntityUid target, VgLimb limb, string limbName, EntityUid targetIdentity)
    {
        if ((limb.Status & VgLimbStatus.Splinted) != 0)
        {
            Self(user, Loc.GetString("vg-medical-already-splinted", ("target", targetIdentity), ("limb", limbName)));
            return;
        }

        Visible(user, target, Loc.GetString(user == target ? "vg-medical-splint-start-self" : "vg-medical-splint-start",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", targetIdentity),
            ("item", ent.Owner),
            ("limb", limbName)));

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.SplintDelay, new VgSplintDoAfterEvent(limb.Id), ent, target, ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnSplintDoAfter(Entity<VgMedicalComponent> ent, ref VgSplintDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || !TryComp<VgBodyComponent>(target, out var body))
            return;

        args.Handled = true;
        if (!_body.TryGetLimb((target, body), args.Zone, out var limb) || !limb.IsExisting)
            return;

        var user = args.User;
        var userIdentity = Identity.Entity(user, EntityManager);
        var limbName = Loc.GetString(limb.Name);

        if (user == target)
        {
            var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(user).Id, GetNetEntity(ent).Id);
            if (!random.Prob(SelfSplintChance))
            {
                Visible(user, target, Loc.GetString("vg-medical-splint-fumble", ("user", userIdentity), ("item", ent.Owner)));
                return;
            }

            Visible(user, target, Loc.GetString("vg-medical-splint-done-self", ("user", userIdentity), ("item", ent.Owner), ("limb", limbName)));
        }
        else
        {
            Visible(user, target, Loc.GetString("vg-medical-splint-done",
                ("user", userIdentity),
                ("target", Identity.Entity(target, EntityManager)),
                ("item", ent.Owner),
                ("limb", limbName)));
        }

        _body.Splint((target, body), limb);
        _stack.Use(ent, 1);
    }

    /// <summary>
    ///     vg13 /obj/item/stack/medical/attack() on anything but a human: heals half the kit's numbers overall.
    /// </summary>
    private void ApplyToSimpleMob(Entity<VgMedicalComponent> ent, EntityUid user, EntityUid target)
    {
        var heal = new DamageSpecifier();
        if (ent.Comp.HealBrute > 0)
            heal += new DamageSpecifier(_proto.Index(BruteGroup), -ent.Comp.HealBrute / 2);
        if (ent.Comp.HealBurn > 0)
            heal += new DamageSpecifier(_proto.Index(BurnGroup), -ent.Comp.HealBurn / 2);

        if (!heal.Empty)
            _damageable.TryChangeDamage(target, heal, true, origin: user);

        Visible(user, target, Loc.GetString("vg-medical-apply",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", Identity.Entity(target, EntityManager)),
            ("item", ent.Owner)));
        _stack.Use(ent, 1);
    }

    private float RollHeal(Entity<VgMedicalComponent> ent, EntityUid user, float amount)
    {
        // vg13 rand(heal, heal + 5), whole numbers.
        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(user).Id, GetNetEntity(ent).Id);
        return amount + random.Next(0, 6);
    }

    private void Self(EntityUid user, string message)
    {
        _popup.PopupClient(message, user, user, PopupType.SmallCaution);
    }

    private void Visible(EntityUid user, EntityUid target, string message)
    {
        _popup.PopupPredicted(message, target, user);
    }
}
