using Content.Shared._VG.Body;
using Content.Shared._VG.Input;
using Content.Shared._VG.Random;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Combat;

/// <summary>
///     vg13 intents and zone selection. Everything is predicted: intent and zone changes come from shared key binds or
///     predictive events, and the click handling runs inside SS14's (predicted) interaction code.
/// </summary>
public sealed class VgCombatSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    private static readonly SoundSpecifier MissSound = new SoundPathSpecifier("/Audio/Weapons/punchmiss.ogg");
    private static readonly SoundSpecifier ThudSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");
    private static readonly SoundSpecifier SlapSound = new SoundPathSpecifier("/Audio/_VG/Effects/slap1.ogg");

    private static readonly ProtoId<DamageTypePrototype> PunchDamageType = "Blunt";

    /// <summary>
    ///     vg13 human species max_hurt_damage: punches do 0-5, and 0 is a miss.
    /// </summary>
    private const int MaxPunchDamage = 5;

    /// <summary>
    ///     vg13 get_knockout_chance() base for humans.
    /// </summary>
    private const float KnockoutChance = 8;

    // vg13 WEAKEN durations are counted in 2 second Life() ticks.
    private static readonly TimeSpan PunchKnockdown = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DisarmKnockdown = TimeSpan.FromSeconds(8);

    // Salts for VgPredictedRandom, so different rolls in one tick don't share numbers.
    private const int SaltDisarm = 1;
    private const int SaltMelee = 2;

    private static readonly HashSet<string> ValidZones = new();

    public override void Initialize()
    {
        base.Initialize();

        foreach (var (_, zone) in VgKeyFunctions.Zones)
        {
            ValidZones.Add(zone);
        }

        SubscribeLocalEvent<VgIntentComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(SharedCombatModeSystem) });
        SubscribeAllEvent<VgSetIntentEvent>(OnSetIntentEvent);
        SubscribeAllEvent<VgSetZoneEvent>(OnSetZoneEvent);

        var binds = CommandBinds.Builder;
        foreach (var (function, intent) in VgKeyFunctions.Intents)
        {
            binds.Bind(function, InputCmdHandler.FromDelegate(s => SetIntent(s?.AttachedEntity, intent), handle: false, outsidePrediction: false));
        }

        foreach (var (function, zone) in VgKeyFunctions.Zones)
        {
            binds.Bind(function, InputCmdHandler.FromDelegate(s => SetZone(s?.AttachedEntity, zone), handle: false, outsidePrediction: false));
        }

        binds.Bind(VgKeyFunctions.VgIntentCycle, InputCmdHandler.FromDelegate(CycleIntent, handle: false, outsidePrediction: false));
        binds.Register<VgCombatSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<VgCombatSystem>();
    }

    private void OnMapInit(Entity<VgIntentComponent> ent, ref MapInitEvent args)
    {
        // Intents replace combat mode: no toggle, and it stays off so it never blocks interactions.
        if (!TryComp<CombatModeComponent>(ent, out var combat))
            return;

        _actions.RemoveAction(ent, combat.CombatToggleActionEntity);
        _combatMode.SetInCombatMode(ent, false, combat);
    }

    private void OnSetIntentEvent(VgSetIntentEvent ev, EntitySessionEventArgs args)
    {
        SetIntent(args.SenderSession.AttachedEntity, ev.Intent);
    }

    private void OnSetZoneEvent(VgSetZoneEvent ev, EntitySessionEventArgs args)
    {
        SetZone(args.SenderSession.AttachedEntity, ev.Zone);
    }

    private void CycleIntent(ICommonSession? session)
    {
        if (TryComp<VgIntentComponent>(session?.AttachedEntity, out var comp))
            SetIntent(session.AttachedEntity, (VgIntent) (((int) comp.Intent + 1) % 4));
    }

    public void SetIntent(EntityUid? uid, VgIntent intent)
    {
        if (!TryComp<VgIntentComponent>(uid, out var comp) || comp.Intent == intent || !Enum.IsDefined(intent))
            return;

        comp.Intent = intent;
        Dirty(uid.Value, comp);
    }

    public void SetZone(EntityUid? uid, string zone)
    {
        if (!TryComp<VgIntentComponent>(uid, out var comp) || comp.Zone == zone || !ValidZones.Contains(zone))
            return;

        comp.Zone = zone;
        Dirty(uid.Value, comp);
    }

    /// <summary>
    ///     Whether this mob attacks through vg13 intents rather than SS14 combat mode.
    /// </summary>
    public bool UsesIntents(EntityUid uid)
    {
        return HasComp<VgIntentComponent>(uid);
    }

    public VgIntent? GetIntent(EntityUid uid)
    {
        return TryComp<VgIntentComponent>(uid, out var comp) ? comp.Intent : null;
    }

    /// <summary>
    ///     The zone this mob is aiming at; the chest for mobs without a zone selector.
    /// </summary>
    public string GetZone(EntityUid uid)
    {
        return TryComp<VgIntentComponent>(uid, out var comp) ? comp.Zone : VgLimbs.Chest;
    }

    /// <summary>
    ///     vg13 gun afterattack()/attack(): a gun fires at anything out of reach whatever the intent. Clicking something in
    ///     reach is an interaction instead (or a pistol whip), except an adjacent mob on harm intent, which gets shot
    ///     point blank.
    /// </summary>
    public bool ShouldFireGun(EntityUid user, EntityCoordinates coordinates, EntityUid? target)
    {
        if (target is { } clicked && clicked != user && _interaction.InRangeUnobstructed(user, clicked))
            return GetIntent(user) == VgIntent.Harm && HasComp<MobStateComponent>(clicked);

        return target != user && !_interaction.InRangeUnobstructed(user, coordinates);
    }

    /// <summary>
    ///     vg13 attack_hand() on a mob: what an empty-handed click does, by intent. Returns false to let SS14's normal
    ///     hand interaction run (help intent, or anything that isn't a mob).
    /// </summary>
    public bool TryUnarmed(EntityUid user, EntityUid target)
    {
        if (!TryComp<VgIntentComponent>(user, out var comp) || target == user || !HasComp<MobStateComponent>(target))
            return false;

        // vg13 delayNextAttack(10): clicking a mob blocks clicking one again for a second.
        var now = _timing.CurTime;
        if (comp.NextUnarmed > now)
            return true;

        comp.NextUnarmed = now + comp.UnarmedDelay;
        Dirty(user, comp);

        switch (comp.Intent)
        {
            case VgIntent.Disarm:
                Disarm(user, target, comp);
                return true;
            case VgIntent.Grab:
                Grab(user, target);
                return true;
            case VgIntent.Harm:
                if (_melee.TryGetWeapon(user, out var weaponUid, out var weapon))
                    _melee.AttemptLightAttack(user, weaponUid, weapon, target);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     vg13 attackby(): an item that nothing else reacted to hits whatever was clicked. Mobs always get hit, as in
    ///     vg13; anything else only on harm intent, so stray clicks don't smash things.
    /// </summary>
    public void ItemFallback(EntityUid user, EntityUid used, EntityUid target)
    {
        if (!TryComp<VgIntentComponent>(user, out var comp) || target == used)
            return;

        var isMob = HasComp<MobStateComponent>(target);
        if (!isMob && (comp.Intent != VgIntent.Harm || !HasComp<DamageableComponent>(target)))
            return;

        // On harm intent a gun shoots an adjacent mob point blank instead of clubbing it.
        if (isMob && comp.Intent == VgIntent.Harm && HasComp<GunComponent>(used))
            return;

        if (!_melee.TryGetWeapon(user, out var weaponUid, out var weapon) || weaponUid != used)
            return;

        _melee.AttemptLightAttack(user, weaponUid, weapon, target);
    }

    /// <summary>
    ///     Called by SS14 melee just before a light attack lands. Aims it at the attacker's selected zone, with vg13's
    ///     miss chances, and swaps unarmed damage for a vg13 punch. Returns false if the attack missed.
    /// </summary>
    public bool TryAimMelee(EntityUid user, EntityUid target, EntityUid weapon, ref DamageSpecifier damage)
    {
        if (!TryComp<VgIntentComponent>(user, out var comp) || !HasComp<MobStateComponent>(target))
            return true;

        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(user).Id, GetNetEntity(target).Id, SaltMelee);

        if (weapon == user)
            return Punch(user, target, comp, random, ref damage);

        if (!TryComp<VgBodyComponent>(target, out var body))
            return true;

        // vg13 carbon/attacked_by(). You can't miss yourself.
        var zone = target == user ? VgLimbs.CheckZone(comp.Zone) : ZoneWithMissChance(comp.Zone, target, random);
        var missingLimb = false;
        if (zone != null && body.Limbs.TryGetValue(zone, out var aimed) && !aimed.IsExisting)
        {
            missingLimb = true;
            zone = null;
        }

        if (zone == null)
        {
            // Someone who's down can't dodge; vg13 then lands the hit on the chest.
            if (_mobState.IsAlive(target))
            {
                var key = missingLimb ? "vg-attack-miss-missing-limb" : "vg-attack-miss-item";
                var limbName = missingLimb && body.Limbs.TryGetValue(VgLimbs.CheckZone(comp.Zone), out var missing)
                    ? Loc.GetString(missing.Name)
                    : string.Empty;
                Visible(user, target, Loc.GetString(key,
                    ("user", Identity.Entity(user, EntityManager)),
                    ("target", Identity.Entity(target, EntityManager)),
                    ("item", weapon),
                    ("limb", limbName)));
                _audio.PlayPredicted(MissSound, target, user);
                return false;
            }

            zone = VgLimbs.Chest;
        }

        damage = damage.WithVgZone(zone);

        if (body.Limbs.TryGetValue(zone, out var limb))
        {
            Visible(user, target, Loc.GetString("vg-attack-hit-item",
                ("user", Identity.Entity(user, EntityManager)),
                ("target", Identity.Entity(target, EntityManager)),
                ("item", weapon),
                ("limb", Loc.GetString(limb.Name))));
        }

        return true;
    }

    /// <summary>
    ///     vg13 unarmed_attack_mob() for humans: 0-5 brute at roughly the aimed zone, with a chance to knock down.
    /// </summary>
    private bool Punch(EntityUid user, EntityUid target, VgIntentComponent comp, System.Random random, ref DamageSpecifier damage)
    {
        var userIdentity = Identity.Entity(user, EntityManager);
        var targetIdentity = Identity.Entity(target, EntityManager);

        var amount = random.Next(0, MaxPunchDamage + 1);
        if (amount == 0)
        {
            Visible(user, target, Loc.GetString("vg-attack-miss-unarmed", ("user", userIdentity), ("target", targetIdentity)));
            _audio.PlayPredicted(MissSound, target, user);
            return false;
        }

        damage = new DamageSpecifier(_proto.Index(PunchDamageType), amount);
        if (HasComp<VgBodyComponent>(target))
            damage = damage.WithVgZone(_body.RandomZone(target, comp.Zone));

        Visible(user, target, Loc.GetString("vg-attack-punch", ("user", userIdentity), ("target", targetIdentity)));

        if (random.Prob(KnockoutChance))
        {
            Visible(user, target, Loc.GetString("vg-attack-knockdown", ("user", userIdentity), ("target", targetIdentity)));
            _stun.TryKnockdown(target, PunchKnockdown, true);
        }

        return true;
    }

    /// <summary>
    ///     vg13 human disarm_mob(): slap the face, or 40% miss, 40% push over, otherwise break their pulls or knock the
    ///     item out of their hand.
    /// </summary>
    private void Disarm(EntityUid user, EntityUid target, VgIntentComponent comp)
    {
        if (!_blocker.CanAttack(user, target, disarm: true))
            return;

        var userIdentity = Identity.Entity(user, EntityManager);
        var targetIdentity = Identity.Entity(target, EntityManager);
        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(user).Id, GetNetEntity(target).Id, SaltDisarm);

        if (comp.Zone == VgLimbs.Head &&
            TryComp<VgBodyComponent>(target, out var body) &&
            body.Limbs.TryGetValue(VgLimbs.Head, out var head) && head.IsExisting)
        {
            Visible(user, target, Loc.GetString("vg-disarm-slap", ("user", userIdentity), ("target", targetIdentity)));
            _audio.PlayPredicted(SlapSound, target, user);
            return;
        }

        if (random.Prob(40))
        {
            Visible(user, target, Loc.GetString("vg-disarm-miss", ("user", userIdentity), ("target", targetIdentity)));
            _audio.PlayPredicted(MissSound, target, user);
            return;
        }

        _audio.PlayPredicted(ThudSound, target, user);

        if (random.Prob(40))
        {
            Visible(user, target, Loc.GetString("vg-disarm-push", ("user", userIdentity), ("target", targetIdentity)));
            _stun.TryKnockdown(target, DisarmKnockdown, true);
            return;
        }

        // Disarming breaks what they're pulling; only if there was nothing to break do they drop their item.
        if (TryComp<PullerComponent>(target, out var puller) &&
            puller.Pulling is { } pulled &&
            TryComp<PullableComponent>(pulled, out var pullable) &&
            _pulling.TryStopPull(pulled, pullable, user))
        {
            Visible(user, target, Loc.GetString("vg-disarm-break-pull",
                ("user", userIdentity),
                ("target", targetIdentity),
                ("pulled", Identity.Entity(pulled, EntityManager))));
            return;
        }

        _hands.TryDrop(target, checkActionBlocker: false);
        Visible(user, target, Loc.GetString("vg-disarm-success", ("user", userIdentity), ("target", targetIdentity)));
    }

    /// <summary>
    ///     vg13 grab_mob(). Until vg13 grab levels are ported, a passive grab is an SS14 pull.
    /// </summary>
    private void Grab(EntityUid user, EntityUid target)
    {
        if (!_pulling.TryStartPull(user, target))
            return;

        _audio.PlayPredicted(ThudSound, target, user);
        Visible(user, target, Loc.GetString("vg-grab-passive",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", Identity.Entity(target, EntityManager))));
    }

    /// <summary>
    ///     vg13 get_zone_with_miss_chance(): returns the zone hit, or null for a clean miss. People lying down or buckled
    ///     can't dodge.
    /// </summary>
    private string? ZoneWithMissChance(string zone, EntityUid target, System.Random random)
    {
        zone = VgLimbs.CheckZone(zone);
        if (_standing.IsDown(target) || _buckle.IsBuckled(target))
            return zone;

        var missChance = zone switch
        {
            VgLimbs.Head => 40,
            VgLimbs.LeftLeg or VgLimbs.RightLeg or VgLimbs.LeftArm or VgLimbs.RightArm => 20,
            VgLimbs.LeftHand or VgLimbs.RightHand or VgLimbs.LeftFoot or VgLimbs.RightFoot => 50,
            _ => 10,
        };

        if (!random.Prob(missChance))
            return zone;

        if (random.Prob(70))
            return null;

        return random.Next(1, 11) switch
        {
            1 => VgLimbs.Head,
            2 => VgLimbs.LeftArm,
            3 => VgLimbs.RightArm,
            4 => VgLimbs.Chest,
            5 => VgLimbs.LeftFoot,
            6 => VgLimbs.RightFoot,
            7 => VgLimbs.LeftHand,
            8 => VgLimbs.RightHand,
            9 => VgLimbs.LeftLeg,
            _ => VgLimbs.RightLeg,
        };
    }

    /// <summary>
    ///     vg13 visible_message() for an attack the attacker's client predicts: they see it right away, and the server
    ///     shows it to everyone else.
    /// </summary>
    private void Visible(EntityUid user, EntityUid target, string message)
    {
        _popup.PopupPredicted(message, target, user, PopupType.MediumCaution);
    }
}
