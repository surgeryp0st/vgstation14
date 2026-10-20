using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Status;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Combat;

/// <summary>
///     vg13 grabs (/obj/item/weapon/grab): passive, aggressive, neck and strangle. A grab rides on an SS14 pull, so the
///     victim is dragged by SS14's (predicted) pull physics and the pull's virtual item in hand is the grab: use it or
///     click the victim with it to tighten, drop it to let go, throw it to throw them.
/// </summary>
public sealed class VgGrabSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private static readonly EntProtoId GrabItem = "VgGrab";
    private static readonly SoundSpecifier GrabSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");
    private static readonly ProtoId<DamageTypePrototype> OxygenDamage = "Asphyxiation";

    /// <summary>
    ///     vg13 UPGRADE_KILL_TIMER.
    /// </summary>
    private static readonly TimeSpan StrangleDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     vg13 delayNextAttack(10) / delayNextMove(10).
    /// </summary>
    private static readonly TimeSpan GrabDelay = TimeSpan.FromSeconds(1);

    private const int SaltEscape = 20;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgGrabberComponent, VgLifeTickEvent>(OnGrabberLifeTick);
        SubscribeLocalEvent<VgGrabberComponent, PullStoppedMessage>(OnPullStopped);
        SubscribeLocalEvent<VgGrabberComponent, BeforeThrowEvent>(OnBeforeThrow);
        SubscribeLocalEvent<VgGrabberComponent, VgGrabStrangleDoAfterEvent>(OnStrangleDoAfter);
        SubscribeLocalEvent<VgGrabbedComponent, UpdateCanMoveEvent>(OnGrabbedUpdateCanMove);
        SubscribeLocalEvent<VgGrabbedComponent, MoveInputEvent>(OnGrabbedMoveInput);
        SubscribeLocalEvent<VgGrabItemComponent, UseInHandEvent>(OnItemUsed);
        SubscribeLocalEvent<VgGrabItemComponent, AfterInteractEvent>(OnItemAfterInteract);
        SubscribeLocalEvent<VgGrabItemComponent, GotUnequippedHandEvent>(OnItemUnequipped);
        SubscribeLocalEvent<VgGrabbedComponent, ComponentShutdown>(OnGrabbedShutdown);
    }

    /// <summary>
    ///     vg13 grab_mob(): a passive grab on <paramref name="target"/>, held in the active hand.
    /// </summary>
    public bool TryGrab(EntityUid user, EntityUid target)
    {
        // Grabbing them again tightens the grab.
        if (TryComp<VgGrabberComponent>(user, out var existing) && existing.Victim == target)
        {
            Upgrade((user, existing));
            return true;
        }

        // vg13 grab_check(): not yourself, nothing anchored, not while lying down; and pacifists don't grab.
        if (target == user || Transform(target).Anchored || _standing.IsDown(user) || !_blocker.CanAttack(user, target))
            return false;

        var targetIdentity = Identity.Entity(target, EntityManager);
        if (_buckle.IsBuckled(target))
        {
            _popup.Self(user, Loc.GetString("vg-grab-buckled", ("target", targetIdentity)), user);
            return false;
        }

        // One grab at a time, until vg13's multiple grabs are needed.
        if (HasComp<VgGrabberComponent>(user) || HasComp<VgGrabbedComponent>(target))
            return false;

        if (!_pulling.TryStartPull(user, target))
            return false;

        var grabber = EnsureComp<VgGrabberComponent>(user);
        grabber.Victim = target;
        grabber.State = VgGrabState.Passive;

        // vg13's grab is an item in the active hand. The server spawns it; the engine can't predict spawns.
        if (_net.IsServer)
        {
            var item = Spawn(GrabItem, Transform(user).Coordinates);
            var itemComp = Comp<VgGrabItemComponent>(item);
            itemComp.Grabber = user;
            itemComp.Victim = target;
            Dirty(item, itemComp);
            grabber.Item = item;
            UpdateVisuals((user, grabber));

            if (_hands.GetActiveHand(user) is not { } hand || !_hands.TryPickup(user, item, hand))
                _hands.TryPickupAnyHand(user, item);
        }

        Dirty(user, grabber);
        _speed.RefreshMovementSpeedModifiers(user);

        var grabbed = EnsureComp<VgGrabbedComponent>(target);
        grabbed.Grabber = user;
        Dirty(target, grabbed);

        _audio.PlayPredicted(GrabSound, target, user);
        Visible(user, user, Loc.GetString("vg-grab-passive",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", targetIdentity)));
        return true;
    }

    public VgGrabState? GetState(EntityUid user, EntityUid victim)
    {
        return TryComp<VgGrabberComponent>(user, out var comp) && comp.Victim == victim ? comp.State : null;
    }

    /// <summary>
    ///     Lets go: ends the grab and the pull under it.
    /// </summary>
    public void Release(Entity<VgGrabberComponent?> grabber)
    {
        if (!Resolve(grabber, ref grabber.Comp, false))
            return;

        var victim = grabber.Comp.Victim;
        var item = grabber.Comp.Item;
        RemComp<VgGrabberComponent>(grabber);
        RemComp<VgGrabbedComponent>(victim);
        DeleteItem(item);
        _speed.RefreshMovementSpeedModifiers(grabber);

        if (TryComp<PullableComponent>(victim, out var pullable) && pullable.Puller == grabber.Owner)
            _pulling.TryStopPull(victim, pullable, grabber);
    }

    /// <summary>
    ///     vg13 grab s_click(): tightens the grab a step.
    /// </summary>
    private void Upgrade(Entity<VgGrabberComponent> ent)
    {
        var user = ent.Owner;
        var victim = ent.Comp.Victim;
        var now = _timing.CurTime;

        if (ent.Comp.State == VgGrabState.Upgrading || now < ent.Comp.NextUpgrade)
            return;

        if (!_blocker.CanMove(user) || _standing.IsDown(user))
        {
            Release((ent.Owner, ent.Comp));
            return;
        }

        var userIdentity = Identity.Entity(user, EntityManager);
        var victimIdentity = Identity.Entity(victim, EntityManager);

        switch (ent.Comp.State)
        {
            case VgGrabState.Passive:
                Visible(user, user, Loc.GetString("vg-grab-aggressive", ("user", userIdentity), ("target", victimIdentity)));
                SetState(ent, VgGrabState.Aggressive);
                break;
            case VgGrabState.Aggressive:
                Visible(user, user, Loc.GetString("vg-grab-neck", ("user", userIdentity), ("target", victimIdentity)));
                SetState(ent, VgGrabState.Neck);
                PullOntoTile(user, victim);
                break;
            case VgGrabState.Neck:
                Visible(user, user, Loc.GetString("vg-grab-strangle-start", ("user", userIdentity), ("target", victimIdentity)));
                SetState(ent, VgGrabState.Upgrading);
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, StrangleDelay, new VgGrabStrangleDoAfterEvent(), user, victim)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                });
                return;
        }

        ent.Comp.NextUpgrade = now + GrabDelay;
        Dirty(ent);
    }

    private void OnStrangleDoAfter(Entity<VgGrabberComponent> ent, ref VgGrabStrangleDoAfterEvent args)
    {
        if (args.Handled || ent.Comp.State != VgGrabState.Upgrading)
            return;

        args.Handled = true;
        var userIdentity = Identity.Entity(ent, EntityManager);
        var victimIdentity = Identity.Entity(ent.Comp.Victim, EntityManager);

        if (args.Cancelled || !_blocker.CanMove(ent) || _standing.IsDown(ent))
        {
            Visible(ent, ent, Loc.GetString("vg-grab-strangle-fail", ("user", userIdentity), ("target", victimIdentity)));
            SetState(ent, VgGrabState.Neck);
            return;
        }

        Visible(ent, ent, Loc.GetString("vg-grab-strangle", ("user", userIdentity), ("target", victimIdentity)));
        SetState(ent, VgGrabState.Kill);
        _status.AddLoseBreath(ent.Comp.Victim, 1);
        ent.Comp.NextUpgrade = _timing.CurTime + GrabDelay;
        Dirty(ent);
    }

    private void SetState(Entity<VgGrabberComponent> ent, VgGrabState state)
    {
        ent.Comp.State = state;
        Dirty(ent);
        UpdateVisuals(ent);
        _blocker.UpdateCanMove(ent.Comp.Victim);
    }

    /// <summary>
    ///     vg13 grab process(), on the grabber's Life() tick.
    /// </summary>
    private void OnGrabberLifeTick(Entity<VgGrabberComponent> ent, ref VgLifeTickEvent args)
    {
        var victim = ent.Comp.Victim;

        // vg13 confirm(): the grab breaks once they're apart.
        if (Deleted(victim) || !_interaction.InRangeUnobstructed(ent.Owner, victim))
        {
            Release((ent.Owner, ent.Comp));
            return;
        }

        if (ent.Comp.State >= VgGrabState.Neck)
        {
            PullOntoTile(ent, victim);

            // Hard to breathe in a headlock.
            _damageable.TryChangeDamage(victim, new DamageSpecifier(_proto.Index(OxygenDamage), 1), true, origin: ent);
        }

        if (ent.Comp.State == VgGrabState.Kill)
        {
            _status.Stun(victim, 5);
            _status.Knockdown(victim, 5);
            _status.Stutter(victim, 6);
            if (_status.AddLoseBreath(victim, 1, 3) >= 3)
                _status.Mute(victim, 2);
        }
    }

    /// <summary>
    ///     Neck grabs hold the victim on the grabber's own tile (vg13 forceMove(assailant.loc)).
    /// </summary>
    private void PullOntoTile(EntityUid user, EntityUid victim)
    {
        if (_buckle.IsBuckled(victim))
            return;

        _transform.SetCoordinates(victim, Transform(user).Coordinates);
    }

    /// <summary>
    ///     vg13 Process_Grab(): an aggressive grab or worse stops the victim moving, though each try has a chance to
    ///     break free. A passive grab just ends when they walk off (SS14's pull already does that).
    /// </summary>
    private void OnGrabbedUpdateCanMove(Entity<VgGrabbedComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running && GetState(ent.Comp.Grabber, ent) >= VgGrabState.Aggressive)
            args.Cancel();
    }

    private void OnGrabbedMoveInput(Entity<VgGrabbedComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement || _timing.ApplyingState)
            return;

        var state = GetState(ent.Comp.Grabber, ent);
        if (state is not (VgGrabState.Aggressive or VgGrabState.Neck))
            return;

        // Can't struggle while stunned or on the floor.
        var now = _timing.CurTime;
        if (now < ent.Comp.NextEscape || HasComp<StunnedComponent>(ent) || _standing.IsDown(ent))
            return;

        ent.Comp.NextEscape = now + GrabDelay;
        Dirty(ent);

        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(ent).Id, SaltEscape);
        if (!random.Prob(state == VgGrabState.Aggressive ? 25 : 5))
            return;

        var key = state == VgGrabState.Aggressive ? "vg-grab-break-free" : "vg-grab-break-headlock";
        Visible(ent, ent, Loc.GetString(key,
            ("target", Identity.Entity(ent, EntityManager)),
            ("user", Identity.Entity(ent.Comp.Grabber, EntityManager))));
        Release(ent.Comp.Grabber);
    }

    /// <summary>
    ///     Using the grab in hand (Z) tightens it.
    /// </summary>
    private void OnItemUsed(Entity<VgGrabItemComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !TryComp<VgGrabberComponent>(args.User, out var grabber) || grabber.Item != ent.Owner)
            return;

        args.Handled = true;
        Upgrade((args.User, grabber));
    }

    /// <summary>
    ///     vg13 grab attack(): clicking the victim with the grab tightens it.
    /// </summary>
    private void OnItemAfterInteract(Entity<VgGrabItemComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != ent.Comp.Victim || !TryComp<VgGrabberComponent>(args.User, out var grabber))
            return;

        args.Handled = true;
        Upgrade((args.User, grabber));
    }

    /// <summary>
    ///     vg13 toss(): throwing an aggressive grab throws the victim; a passive one can't be thrown.
    /// </summary>
    private void OnBeforeThrow(Entity<VgGrabberComponent> ent, ref BeforeThrowEvent args)
    {
        if (args.ItemUid != ent.Comp.Item)
            return;

        if (ent.Comp.State < VgGrabState.Aggressive || _buckle.IsBuckled(ent.Comp.Victim))
        {
            args.Cancelled = true;
            return;
        }

        var victim = ent.Comp.Victim;
        Release((ent.Owner, ent.Comp));
        args.ItemUid = victim;
    }

    private void OnPullStopped(Entity<VgGrabberComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PulledUid != ent.Comp.Victim)
            return;

        var item = ent.Comp.Item;
        RemComp<VgGrabbedComponent>(ent.Comp.Victim);
        RemComp<VgGrabberComponent>(ent);
        DeleteItem(item);
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    /// <summary>
    ///     vg13 grab dropped(): the grab only exists in a hand, so taking it out of one lets go.
    /// </summary>
    private void OnItemUnequipped(Entity<VgGrabItemComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (TryComp<VgGrabberComponent>(ent.Comp.Grabber, out var grabber) && grabber.Item == ent.Owner)
            Release((ent.Comp.Grabber, grabber));
        else
            DeleteItem(ent);
    }

    private void DeleteItem(EntityUid? item)
    {
        // No predicted deletion in this engine version.
        if (_net.IsServer && item != null && !TerminatingOrDeleted(item.Value))
            QueueDel(item.Value);
    }

    private void UpdateVisuals(Entity<VgGrabberComponent> ent)
    {
        if (ent.Comp.Item is { } item)
            _appearance.SetData(item, VgGrabVisuals.State, ent.Comp.State);
    }

    private void OnGrabbedShutdown(Entity<VgGrabbedComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _blocker.UpdateCanMove(ent);
    }

    private void Visible(EntityUid uid, EntityUid predictingUser, string message)
    {
        _popup.Visible(uid, message, message, predictingUser, Content.Shared.Popups.PopupType.MediumCaution);
    }
}
