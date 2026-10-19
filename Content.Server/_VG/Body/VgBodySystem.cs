using Content.Server._VG.Life;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.CCVar;
using Content.Shared._VG.Damage;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._VG.Body;

/// <summary>
///     Runs vgstation13's limb damage model. Brute and burn dealt through <see cref="DamageableSystem"/> are routed onto
///     limbs as wounds, and the mob's brute/burn totals are then rewritten to the sum of its organic limbs, exactly
///     like vg13's getBruteLoss()/getFireLoss().
/// </summary>
public sealed partial class VgBodySystem : SharedVgBodySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    private bool _bonesCanBreak;
    private bool _limbsCanBreak;
    private float _organHealthMultiplier;
    private float _organRegenerationMultiplier;

    /// <summary>
    ///     Set while we write the limb totals back into the damageable, so we ignore our own change.
    /// </summary>
    private bool _syncing;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VgBodyComponent, DamageChangedEvent>(OnDamageChanged, before: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<VgBodyComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<VgBodyComponent, VgLifeTickEvent>(OnLifeTick);

        Subs.CVar(_cfg, VgCCVars.BonesCanBreak, value => _bonesCanBreak = value, true);
        Subs.CVar(_cfg, VgCCVars.LimbsCanBreak, value => _limbsCanBreak = value, true);
        Subs.CVar(_cfg, VgCCVars.OrganHealthMultiplier, value => _organHealthMultiplier = value, true);
        Subs.CVar(_cfg, VgCCVars.OrganRegenerationMultiplier, value => _organRegenerationMultiplier = value, true);
    }

    private void OnMapInit(Entity<VgBodyComponent> ent, ref MapInitEvent args)
    {
        // Mobs can be spawned already hurt (e.g. corpses on maps); put that damage on their limbs.
        if (TryComp<DamageableComponent>(ent, out var damageable))
            SyncLimbs(ent, damageable, null);
    }

    private void OnDamageChanged(Entity<VgBodyComponent> ent, ref DamageChangedEvent args)
    {
        if (_syncing)
            return;

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

        ent.Comp.NumberWounds = 0;
        Dirty(ent);
        WriteBackTotals(ent);
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

    private bool HasBlood(EntityUid uid)
    {
        return HasComp<BloodstreamComponent>(uid);
    }

    /// <summary>
    ///     vg13 custom_pain(): a pain message only the owner sees.
    /// </summary>
    private void CustomPain(EntityUid uid, string message)
    {
        _popup.PopupEntity(message, uid, uid, PopupType.MediumCaution);
    }

    /// <summary>
    ///     vg13 visible_message(): one message for the mob itself, another for everyone else.
    /// </summary>
    private void VisibleMessage(EntityUid uid, string others, string self, PopupType type = PopupType.MediumCaution)
    {
        _popup.PopupEntity(others, uid, Filter.PvsExcept(uid), true, type);
        _popup.PopupEntity(self, uid, uid, type);
    }

    private void Scream(EntityUid uid)
    {
        _chat.TryEmoteWithChat(uid, "Scream", ignoreActionBlocker: true);
    }
}
