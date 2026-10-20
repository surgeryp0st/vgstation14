using System.Diagnostics.CodeAnalysis;
using Content.Shared._VG.CCVar;
using Content.Shared._VG.Damage;
using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Status;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Body;

/// <summary>
///     Runs vgstation13's limb damage model. Brute and burn dealt through <see cref="DamageableSystem"/> are routed onto
///     limbs as wounds, and the mob's brute/burn totals are then rewritten to the sum of its organic limbs, exactly
///     like vg13's getBruteLoss()/getFireLoss().
/// </summary>
/// <remarks>
///     Everything here is predicted: the attacker's client predicts the limb damage from its hit, and a player's client
///     predicts its own mob's Life() tick. Randomness comes from <see cref="PredictedRandom"/>, seeded the same way on
///     both sides, and popups/sounds go through <see cref="PredictingUser"/> so nobody gets them twice.
/// </remarks>
public abstract partial class SharedVgBodySystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager Prototypes = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly INetManager Net = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly VgPopupSystem _vgPopup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    private bool _bonesCanBreak;
    private bool _limbsCanBreak;
    private float _organHealthMultiplier;
    private float _organRegenerationMultiplier;

    /// <summary>
    ///     Set while we write the limb totals back into the damageable, so we ignore our own change.
    /// </summary>
    private bool _syncing;

    /// <summary>
    ///     RNG for the current operation, seeded identically on server and client by <see cref="BeginPrediction"/>.
    /// </summary>
    protected System.Random PredictedRandom = new();

    /// <summary>
    ///     The player whose client predicts the current operation (the attacker, or the mob on its own Life() tick).
    ///     The server leaves them out of popups and sounds because they already saw them.
    /// </summary>
    protected EntityUid? PredictingUser;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<VgBodyComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<VgBodyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VgBodyComponent, DamageChangedEvent>(OnDamageChanged, before: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<VgBodyComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<VgBodyComponent, VgLifeTickEvent>(OnLifeTick);

        InitializeExamine();

        Subs.CVar(_cfg, VgCCVars.BonesCanBreak, value => _bonesCanBreak = value, true);
        Subs.CVar(_cfg, VgCCVars.LimbsCanBreak, value => _limbsCanBreak = value, true);
        Subs.CVar(_cfg, VgCCVars.OrganHealthMultiplier, value => _organHealthMultiplier = value, true);
        Subs.CVar(_cfg, VgCCVars.OrganRegenerationMultiplier, value => _organRegenerationMultiplier = value, true);
    }

    private void OnInit(Entity<VgBodyComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Limbs.Count > 0)
            return;

        if (ent.Comp.Prototype.Id == null || !Prototypes.TryIndex(ent.Comp.Prototype, out var proto))
            return;

        foreach (var template in proto.Limbs)
        {
            ent.Comp.Limbs[template.Id] = new VgLimb(template);
        }

        foreach (var limb in ent.Comp.Limbs.Values)
        {
            if (limb.Parent != null && ent.Comp.Limbs.TryGetValue(limb.Parent, out var parent))
                parent.Children.Add(limb.Id);
        }

        foreach (var template in proto.Organs)
        {
            ent.Comp.Organs[template.Id] = new VgOrgan(template);
        }
    }

    /// <summary>
    ///     The generated state handler shallow-copies the dictionary. Prediction mutates limbs in place, which would also
    ///     change the server state the client rolls back to, so give the component its own copies.
    /// </summary>
    private void OnHandleState(Entity<VgBodyComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        foreach (var id in new List<string>(ent.Comp.Limbs.Keys))
        {
            ent.Comp.Limbs[id] = new VgLimb(ent.Comp.Limbs[id]);
        }

        foreach (var id in new List<string>(ent.Comp.Organs.Keys))
        {
            ent.Comp.Organs[id] = new VgOrgan(ent.Comp.Organs[id]);
        }
    }

    private void OnMapInit(Entity<VgBodyComponent> ent, ref MapInitEvent args)
    {
        // Mobs can be spawned already hurt (e.g. corpses on maps); put that damage on their limbs.
        BeginPrediction(ent, null, 1);
        if (TryComp<DamageableComponent>(ent, out var damageable))
            SyncLimbs(ent, damageable, null);
    }

    private void OnDamageChanged(Entity<VgBodyComponent> ent, ref DamageChangedEvent args)
    {
        // The client re-raises this while applying server state; the limbs come with that state already.
        if (_syncing || Timing.ApplyingState)
            return;

        BeginPrediction(ent, args.Origin, 2);
        SyncLimbs(ent, args.Damageable, args.DamageDelta);
    }

    private void OnRejuvenate(Entity<VgBodyComponent> ent, ref RejuvenateEvent args)
    {
        foreach (var limb in ent.Comp.Limbs.Values)
        {
            // vg13 rejuvenate(): robotic limbs stay robotic.
            limb.Status &= VgLimbStatus.Robot | VgLimbStatus.Peg;
            limb.PermaInjury = 0;
            limb.BruteDamage = 0;
            limb.BurnDamage = 0;
            limb.GermLevel = 0;
            limb.Wounds.Clear();
            limb.NumberWounds = 0;
            limb.InternallyBleeding = false;
            limb.Amputated = false;
            limb.DroppedSpawned = false;
            limb.Open = 0;
        }

        // vg13 organ rejuvenate().
        foreach (var organ in ent.Comp.Organs.Values)
        {
            organ.Damage = 0;
            organ.GermLevel = 0;
        }

        ent.Comp.BrainLoss = 0;
        ent.Comp.NumberWounds = 0;
        Dirty(ent);
        WriteBackTotals(ent);
    }

    /// <summary>
    ///     Starts a predicted operation on a body: seeds <see cref="PredictedRandom"/> from the tick and entity so the
    ///     server and the predicting client roll the same numbers, and records who is predicting it.
    /// </summary>
    /// <param name="salt">Separates different kinds of operation in the same tick.</param>
    protected void BeginPrediction(EntityUid uid, EntityUid? user, int salt)
    {
        PredictedRandom = CreatePredictedRandom(uid, salt);
        PredictingUser = user;
    }

    private System.Random CreatePredictedRandom(EntityUid uid, int salt)
    {
        return VgPredictedRandom.Create(Timing.CurTick, GetNetEntity(uid).Id, salt);
    }

    public bool TryGetLimb(Entity<VgBodyComponent?> ent, string id, [NotNullWhen(true)] out VgLimb? limb)
    {
        limb = null;
        return Resolve(ent, ref ent.Comp, false) && ent.Comp.Limbs.TryGetValue(id, out limb);
    }

    /// <summary>
    ///     Gets the limb a zone refers to (vg13 get_organ(check_zone(zone))).
    /// </summary>
    public bool TryGetLimbForZone(Entity<VgBodyComponent?> ent, string? zone, [NotNullWhen(true)] out VgLimb? limb)
    {
        return TryGetLimb(ent, VgLimbs.CheckZone(zone), out limb);
    }

    public VgLimb? GetParent(VgBodyComponent body, VgLimb limb)
    {
        return limb.Parent != null ? body.Limbs.GetValueOrDefault(limb.Parent) : null;
    }

    /// <summary>
    ///     vg13 ran_zone(): usually hits the aimed zone, otherwise strays towards the chest. Predicted per target.
    /// </summary>
    public string RandomZone(EntityUid target, string? zone, float probability = 90)
    {
        zone = VgLimbs.CheckZone(zone);
        if (probability >= 100)
            return zone;

        var random = CreatePredictedRandom(target, 3);
        if (zone == VgLimbs.Chest)
        {
            if (random.NextDouble() * 100 < probability)
                return VgLimbs.Chest;

            return random.Next(1, 10) switch
            {
                <= 3 => VgLimbs.Head,
                <= 6 => VgLimbs.LeftArm,
                _ => VgLimbs.RightArm,
            };
        }

        return random.NextDouble() * 100 < probability * 0.75f ? zone : VgLimbs.Chest;
    }

    /// <summary>
    ///     Sums brute and burn over existing organic limbs, which is what counts towards health (vg13 updatehealth()).
    /// </summary>
    public (float Brute, float Burn) GetOrganicDamage(VgBodyComponent body)
    {
        var brute = 0f;
        var burn = 0f;
        foreach (var limb in body.Limbs.Values)
        {
            if (!limb.IsOrganic || !limb.IsExisting)
                continue;

            brute += limb.BruteDamage;
            burn += limb.BurnDamage;
        }

        return (brute, burn);
    }

    /// <summary>
    ///     Makes the limbs match the brute/burn totals in the damageable, then writes the limb sums back.
    ///     New damage goes to the zone in <paramref name="delta"/>, or is spread over random limbs if it has none.
    /// </summary>
    private void SyncLimbs(Entity<VgBodyComponent> ent, DamageableComponent damageable, DamageSpecifier? delta)
    {
        var dict = damageable.Damage.DamageDict;
        if (!dict.TryGetValue(VgDamageTranslation.Brute, out var targetBrute) |
            !dict.TryGetValue(VgDamageTranslation.Burn, out var targetBurn))
        {
            return;
        }

        var (brute, burn) = GetOrganicDamage(ent.Comp);
        var bruteDiff = targetBrute.Float() - brute;
        var burnDiff = targetBurn.Float() - burn;

        if (MathF.Abs(bruteDiff) < 0.01f && MathF.Abs(burnDiff) < 0.01f)
            return;

        var takeBrute = MathF.Max(0, bruteDiff);
        var takeBurn = MathF.Max(0, burnDiff);
        if (takeBrute > 0 || takeBurn > 0)
        {
            var sharp = delta?.VgSharpness ?? 0f;
            var edge = delta?.VgEdge ?? false;

            if (delta?.VgZone is { } zone)
                ApplyDamage(ent, zone, takeBrute, takeBurn, sharp, edge);
            else
                TakeOverallDamage(ent, takeBrute, takeBurn, sharp, edge);
        }

        var healBrute = MathF.Max(0, -bruteDiff);
        var healBurn = MathF.Max(0, -burnDiff);
        if (healBrute > 0 || healBurn > 0)
            HealOverallDamage(ent, healBrute, healBurn);

        Dirty(ent);
        WriteBackTotals(ent, damageable);
    }

    /// <summary>
    ///     Sets the damageable's brute/burn to the sum of the organic limbs (vg13 updatehealth()).
    /// </summary>
    private void WriteBackTotals(Entity<VgBodyComponent> ent, DamageableComponent? damageable = null)
    {
        if (!Resolve(ent, ref damageable, false))
            return;

        var dict = damageable.Damage.DamageDict;
        var (brute, burn) = GetOrganicDamage(ent.Comp);
        var changed = false;

        if (dict.TryGetValue(VgDamageTranslation.Brute, out var oldBrute) && oldBrute != FixedPoint2.New(brute))
        {
            dict[VgDamageTranslation.Brute] = FixedPoint2.New(brute);
            changed = true;
        }

        if (dict.TryGetValue(VgDamageTranslation.Burn, out var oldBurn) && oldBurn != FixedPoint2.New(burn))
        {
            dict[VgDamageTranslation.Burn] = FixedPoint2.New(burn);
            changed = true;
        }

        if (!changed)
            return;

        _syncing = true;
        try
        {
            _damageable.DamageChanged(ent, damageable);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Kill(EntityUid uid)
    {
        _mobState.ChangeMobState(uid, MobState.Dead);
    }

    /// <summary>
    ///     vg13 custom_pain(): a pain message only the owner sees.
    /// </summary>
    private void CustomPain(EntityUid uid, string message)
    {
        _status.CustomPain(uid, message, PredictingUser);
    }

    /// <summary>
    ///     vg13 visible_message(): one message for the mob itself, another for everyone else. Either can be null.
    /// </summary>
    private void VisibleMessage(EntityUid uid, string? others, string? self, PopupType type = PopupType.MediumCaution)
    {
        _vgPopup.Visible(uid, others, self, PredictingUser, type);
    }

    /// <summary>
    ///     vg13 emote("scream"). Emotes are server-only until chat is predicted.
    /// </summary>
    protected virtual void Scream(EntityUid uid)
    {
    }

    /// <summary>
    ///     Removes vg13 units of blood from the mob. The bloodstream is server-only until vg13 blood is ported.
    /// </summary>
    protected virtual void RemoveBlood(EntityUid uid, float amount)
    {
    }

    /// <summary>
    ///     DM prob(): a percent chance, where anything over 100 always happens.
    /// </summary>
    private bool Prob(float percent)
    {
        return PredictedRandom.Prob(percent);
    }
}
