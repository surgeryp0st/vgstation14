using Content.Shared._VG.Body;
using Content.Shared._VG.Damage;
using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Species;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Drugs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Rejuvenate;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Speech.Muting;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Status;

/// <summary>
///     vg13's stun model: Stun/Knockdown/Paralyse counted in Life() ticks, halloss, traumatic shock and pain messages.
///     Stuns map onto SS14's (predicted) status effects. Everything runs in shared code; the Life() tick parts are
///     predicted by the mob's own client.
/// </summary>
public sealed class VgStatusSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly Robust.Shared.Network.INetManager _net = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly SharedStutteringSystem _stutter = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;
    [Dependency] private readonly BlurryVisionSystem _blurry = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly Content.Shared.Standing.StandingStateSystem _standing = default!;

    /// <summary>
    ///     vg13 counts stuns in Life() ticks, which come every 2 seconds.
    /// </summary>
    public static readonly TimeSpan LifeTick = TimeSpan.FromSeconds(2);

    private const string StunKey = "Stun";
    private const string KnockdownKey = "KnockedDown";
    private const string SleepKey = "ForcedSleep";
    private const string SlurKey = "SlurredSpeech";

    /// <summary>
    ///     How long one confused step lasts, s: roughly vg13's running move delay.
    /// </summary>
    private const double ConfusedStep = 0.25;

    // Salts for VgPredictedRandom.
    private const int SaltLife = 10;
    private const int SaltConfused = 11;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgStatusComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<VgStatusComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<VgStatusComponent, GetBlurEvent>(OnGetBlur);
        SubscribeLocalEvent<VgStatusComponent, VgMoveDirectionEvent>(OnMoveDirection);
        SubscribeLocalEvent<VgStunOnCollideComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnRejuvenate(Entity<VgStatusComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Halloss = 0;
        ent.Comp.ShockStage = 0;
        ent.Comp.LoseBreath = 0;
        ent.Comp.EyeBlurry = 0;
        ent.Comp.Dizziness = 0;
        ent.Comp.Confused = 0;
        ent.Comp.SleepingUntil = TimeSpan.Zero;
        ent.Comp.ParalysedUntil = TimeSpan.Zero;
        Dirty(ent);
        _blurry.UpdateBlurMagnitude(ent.Owner);
    }

    #region Stuns

    /// <summary>
    ///     vg13 Stun(): can't move or act, for at least this many Life() ticks.
    /// </summary>
    public void Stun(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (!Longer(uid, StunKey, time))
            return;

        _stun.TryStun(uid, time, true);
    }

    /// <summary>
    ///     vg13 Knockdown() (Weaken() in older code): lying on the floor for at least this many Life() ticks.
    /// </summary>
    public void Knockdown(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (!Longer(uid, KnockdownKey, time))
            return;

        _stun.TryKnockdown(uid, time, true);
    }

    /// <summary>
    ///     vg13 Paralyse(): out cold for at least this many Life() ticks.
    /// </summary>
    public void Paralyse(EntityUid uid, float ticks)
    {
        Knockdown(uid, ticks);
        Stun(uid, ticks);

        if (!TryComp<VgStatusComponent>(uid, out var comp))
        {
            // Mobs without vg13 statuses still go out cold.
            var time = ticks * LifeTick;
            if (Longer(uid, SleepKey, time))
                _statusEffects.TryAddStatusEffect<ForcedSleepingComponent>(uid, SleepKey, time, true);
            return;
        }

        var until = _timing.CurTime + ticks * LifeTick;
        if (until > comp.ParalysedUntil)
        {
            comp.ParalysedUntil = until;
            Dirty(uid, comp);
        }

        RefreshSleep((uid, comp));
    }

    /// <summary>
    ///     vg13 <c>sleeping = max(sleeping, ticks)</c>: asleep for at least this many Life() ticks.
    /// </summary>
    public void Sleep(EntityUid uid, float ticks)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        var until = _timing.CurTime + ticks * LifeTick;
        if (until > comp.SleepingUntil)
        {
            comp.SleepingUntil = until;
            Dirty(uid, comp);
        }

        RefreshSleep((uid, comp));
    }

    /// <summary>
    ///     vg13 <c>sleeping = min(sleeping + ticks, max)</c>.
    /// </summary>
    public void AddSleeping(EntityUid uid, float ticks, float max = float.MaxValue)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        var now = _timing.CurTime;
        var current = comp.SleepingUntil > now ? comp.SleepingUntil - now : TimeSpan.Zero;
        var ticksNow = (float) (current / LifeTick);
        comp.SleepingUntil = now + MathF.Max(ticksNow, MathF.Min(ticksNow + ticks, max)) * LifeTick;
        Dirty(uid, comp);
        RefreshSleep((uid, comp));
    }

    public bool IsAsleep(EntityUid uid)
    {
        return TryComp<VgStatusComponent>(uid, out var comp) && comp.SleepingUntil > _timing.CurTime;
    }

    /// <summary>
    ///     vg13 help_shake_act() on someone lying down: ten ticks less sleep, three less paralysis, stun and knockdown.
    /// </summary>
    public void Shake(EntityUid uid)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        var now = _timing.CurTime;
        comp.SleepingUntil = Max(now, comp.SleepingUntil - 10 * LifeTick);
        comp.ParalysedUntil = Max(now, comp.ParalysedUntil - 3 * LifeTick);
        Dirty(uid, comp);
        RefreshSleep((uid, comp));

        _statusEffects.TryRemoveTime(uid, StunKey, 3 * LifeTick);
        _statusEffects.TryRemoveTime(uid, KnockdownKey, 3 * LifeTick);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b)
    {
        return a > b ? a : b;
    }

    /// <summary>
    ///     SS14's forced sleep lasts as long as the longer of vg13 sleep and paralysis.
    /// </summary>
    private void RefreshSleep(Entity<VgStatusComponent> ent)
    {
        var now = _timing.CurTime;
        var end = Max(ent.Comp.SleepingUntil, ent.Comp.ParalysedUntil);
        if (end <= now)
        {
            _statusEffects.TryRemoveStatusEffect(ent, SleepKey);
            return;
        }

        if (_statusEffects.HasStatusEffect(ent, SleepKey))
            _statusEffects.TrySetTime(ent, SleepKey, end - now);
        else
            _statusEffects.TryAddStatusEffect<ForcedSleepingComponent>(ent, SleepKey, end - now, true);
    }

    /// <summary>
    ///     vg13 apply_effect(STUTTER): stuttering for at least this many Life() ticks.
    /// </summary>
    public void Stutter(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (Longer(uid, "Stutter", time))
            _stutter.DoStutter(uid, time, true);
    }

    /// <summary>
    ///     vg13 slurring: drunken speech for at least this many Life() ticks, through SS14's slurred speech. Speech is
    ///     server-side, and so is the accent.
    /// </summary>
    public void Slur(EntityUid uid, float ticks)
    {
        if (_net.IsClient)
            return;

        var time = ticks * LifeTick;
        if (Longer(uid, SlurKey, time))
            _statusEffects.TryAddStatusEffect(uid, SlurKey, time, true, "SlurredAccent");
    }

    public bool IsSlurring(EntityUid uid)
    {
        return _statusEffects.HasStatusEffect(uid, SlurKey);
    }

    /// <summary>
    ///     vg13 silent: can't speak for at least this many Life() ticks.
    /// </summary>
    public void Mute(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (Longer(uid, "Muted", time))
            _statusEffects.TryAddStatusEffect<MutedComponent>(uid, "Muted", time, true);
    }

    /// <summary>
    ///     vg13 eye_blind: can't see for at least this many Life() ticks.
    /// </summary>
    public void Blind(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (Longer(uid, "TemporaryBlindness", time))
            _statusEffects.TryAddStatusEffect<TemporaryBlindnessComponent>(uid, "TemporaryBlindness", time, true);
    }

    /// <summary>
    ///     vg13 Jitter(): shaking for at least this many Life() ticks.
    /// </summary>
    public void Jitter(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (Longer(uid, "Jitter", time))
            _jitter.DoJitter(uid, time, true);
    }

    /// <summary>
    ///     vg13 druggy: seeing things for at least this many Life() ticks.
    /// </summary>
    public void Druggy(EntityUid uid, float ticks)
    {
        var time = ticks * LifeTick;
        if (Longer(uid, "SeeingRainbows", time))
            _statusEffects.TryAddStatusEffect<SeeingRainbowsComponent>(uid, "SeeingRainbows", time, true);
    }

    /// <summary>
    ///     vg13 drowsyness += ticks, which the Life() tick works off.
    /// </summary>
    public void AdjustDrowsiness(Entity<VgStatusComponent?> ent, int ticks)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Drowsiness = Math.Max(0, ent.Comp.Drowsiness + ticks);
        Dirty(ent);
    }

    public int GetLoseBreath(EntityUid uid)
    {
        return TryComp<VgStatusComponent>(uid, out var comp) ? comp.LoseBreath : 0;
    }

    /// <summary>
    ///     vg13 losebreath += amount, capped at <paramref name="max"/>. Returns the new value.
    /// </summary>
    public int AddLoseBreath(EntityUid uid, int amount, int max = int.MaxValue)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return 0;

        comp.LoseBreath = Math.Min(comp.LoseBreath + amount, max);
        Dirty(uid, comp);
        return comp.LoseBreath;
    }

    /// <summary>
    ///     vg13 reagent pain_resistance, summed over everything in the body.
    /// </summary>
    public void SetPainResistance(EntityUid uid, float value)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp) || MathF.Abs(comp.PainResistance - value) < 0.001f)
            return;

        comp.PainResistance = value;
        Dirty(uid, comp);
    }

    /// <summary>
    ///     vg13 pain_numb = max(<paramref name="ticks"/>, pain_numb).
    /// </summary>
    public void NumbPain(EntityUid uid, int ticks)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        comp.PainNumb = Math.Max(comp.PainNumb, ticks);
        Dirty(uid, comp);
    }

    /// <summary>
    ///     vg13 pain_shock_stage += amount, not below 0.
    /// </summary>
    public void AdjustShockStage(EntityUid uid, int amount)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        comp.ShockStage = Math.Max(0, comp.ShockStage + amount);
        Dirty(uid, comp);
    }

    public bool IsParalysed(EntityUid uid)
    {
        return _statusEffects.HasStatusEffect(uid, SleepKey) || HasComp<SleepingComponent>(uid);
    }

    /// <summary>
    ///     vg13 stuns take the longer of the old and new durations (Stun(x) = max(stunned, x)).
    /// </summary>
    private bool Longer(EntityUid uid, string key, TimeSpan time)
    {
        if (!_statusEffects.TryGetTime(uid, key, out var current))
            return true;

        return current.Value.Item2 - _timing.CurTime < time;
    }

    #endregion

    #region Halloss

    public void AdjustHalloss(EntityUid uid, float amount, VgStatusComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        comp.Halloss = MathF.Max(0, comp.Halloss + amount);
        Dirty(uid, comp);
    }

    public float GetHalloss(EntityUid uid)
    {
        return TryComp<VgStatusComponent>(uid, out var comp) ? comp.Halloss : 0;
    }

    /// <summary>
    ///     Stamina doesn't exist for vg13 mobs: called from SS14's stamina code before stamina damage lands. A stun baton
    ///     (<see cref="VgStunOnHitComponent"/>) stuns the vg13 way; anything else deals its stamina damage as halloss.
    ///     Returns true if the stamina damage was taken over.
    /// </summary>
    public bool TryConvertStamina(EntityUid uid, float value, EntityUid? with)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return false;

        if (value > 0 && TryComp<VgStunOnHitComponent>(with, out var baton))
        {
            // vg13 apply_baton_effect().
            Knockdown(uid, baton.StunForce);
            Stun(uid, baton.StunForce);
            Stutter(uid, baton.Stutter);
            return true;
        }

        AdjustHalloss(uid, value, comp);
        return true;
    }

    /// <summary>
    ///     vg13 projectile stun/weaken/agony/stutter, e.g. the taser's electrode.
    /// </summary>
    private void OnProjectileHit(Entity<VgStunOnCollideComponent> ent, ref ProjectileHitEvent args)
    {
        if (!HasComp<VgStatusComponent>(args.Target))
            return;

        Stun(args.Target, ent.Comp.Stun);
        Knockdown(args.Target, ent.Comp.Weaken);
        Stutter(args.Target, ent.Comp.Stutter);
        AdjustHalloss(args.Target, ent.Comp.Agony);
    }

    #endregion

    #region Life

    private void OnLifeTick(Entity<VgStatusComponent> ent, ref VgLifeTickEvent args)
    {
        // vg13 handle_chemicals_in_body(): remove_confused(1), dead or not.
        if (ent.Comp.Confused > 0)
            ent.Comp.Confused = MathF.Max(0, ent.Comp.Confused - 1);

        if (_mobState.IsDead(ent))
        {
            Dirty(ent);
            return;
        }

        // vg13 handle_dizziness(): resting settles it five times as fast.
        if (ent.Comp.Dizziness > 0)
            ent.Comp.Dizziness = MathF.Max(0, ent.Comp.Dizziness - (_standing.IsDown(ent) ? 5 : 1));

        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(ent).Id, SaltLife);

        if (ent.Comp.PainNumb > 0)
            ent.Comp.PainNumb--;

        // vg13: drowsy eyes, and now and then you nod off.
        if (ent.Comp.Drowsiness > 0)
        {
            ent.Comp.Drowsiness--;
            ent.Comp.EyeBlurry = MathF.Max(2, ent.Comp.EyeBlurry);
            if (random.Prob(5))
                AddSleeping(ent, 1);
        }

        HandleHalloss(ent);
        HandleShock(ent, args.LifeTick, random);
        HandlePain(ent, random);
        HandleEyeBlurry(ent);

        Dirty(ent);
        _blurry.UpdateBlurMagnitude(ent.Owner);
    }

    /// <summary>
    ///     vg13 handle_regular_status_updates() for the eyes: no eyes, no blur; blindness holds it; a blindfold rests
    ///     the eyes three times as fast.
    /// </summary>
    private void HandleEyeBlurry(Entity<VgStatusComponent> ent)
    {
        if (ent.Comp.EyeBlurry <= 0)
            return;

        if (TryComp<VgBodyComponent>(ent, out var body) && !body.Organs.ContainsKey(VgOrgans.Eyes))
        {
            ent.Comp.EyeBlurry = 0;
            return;
        }

        if (_statusEffects.HasStatusEffect(ent, "TemporaryBlindness"))
            return;

        var blindfolded = _inventory.TryGetSlotEntity(ent, "eyes", out var glasses) && HasComp<BlindfoldComponent>(glasses);
        ent.Comp.EyeBlurry = MathF.Max(0, ent.Comp.EyeBlurry - (blindfolded ? 3 : 1));
    }

    /// <summary>
    ///     vg13 enable_blurriness(): a blur of eye_blurry / 10, between 0.7 and 1.1, here on SS14's 0-6 blur scale.
    /// </summary>
    private void OnGetBlur(Entity<VgStatusComponent> ent, ref GetBlurEvent args)
    {
        if (ent.Comp.EyeBlurry > 0)
            args.Blur += Math.Clamp(ent.Comp.EyeBlurry / 10, 0.7f, 1.1f) * 3;
    }

    /// <summary>
    ///     vg13 Dizzy(): at least this dizzy.
    /// </summary>
    public void Dizzy(EntityUid uid, float amount)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp) || comp.Dizziness >= amount)
            return;

        comp.Dizziness = amount;
        Dirty(uid, comp);
    }

    /// <summary>
    ///     vg13 <c>dizziness = amount</c> and <c>confused = amount</c>, as standing in a fire does.
    /// </summary>
    public void SetDizzyAndConfused(EntityUid uid, float dizziness, float confused)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        comp.Dizziness = dizziness;
        comp.Confused = confused;
        Dirty(uid, comp);
    }

    /// <summary>
    ///     At least this many Life() ticks of confusion.
    /// </summary>
    public void Confuse(EntityUid uid, float ticks)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp) || comp.Confused >= ticks)
            return;

        comp.Confused = ticks;
        Dirty(uid, comp);
    }

    public float GetDizziness(EntityUid uid)
    {
        return CompOrNull<VgStatusComponent>(uid)?.Dizziness ?? 0;
    }

    /// <summary>
    ///     vg13 <c>confused += ticks</c>.
    /// </summary>
    public void AddConfused(EntityUid uid, float ticks)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        comp.Confused += ticks;
        Dirty(uid, comp);
    }

    public float GetConfused(EntityUid uid)
    {
        return CompOrNull<VgStatusComponent>(uid)?.Confused ?? 0;
    }

    /// <summary>
    ///     vg13 process_confused(): each step goes diagonally to one side or the other of where you meant (a straight
    ///     step becomes a diagonal, a diagonal one straight), a fresh pick every step. Here a step is a quarter second,
    ///     picked the same way on the server and the predicting client.
    /// </summary>
    private void OnMoveDirection(Entity<VgStatusComponent> ent, ref VgMoveDirectionEvent args)
    {
        if (ent.Comp.Confused <= 0)
            return;

        var step = (uint) (_timing.CurTime.TotalSeconds / ConfusedStep);
        var random = VgPredictedRandom.Create(new GameTick(step), GetNetEntity(ent).Id, SaltConfused);
        var angle = new Angle(random.Next(2) == 0 ? Math.PI / 4 : -Math.PI / 4);
        args.Walk = angle.RotateVec(args.Walk);
        args.Sprint = angle.RotateVec(args.Sprint);
    }

    /// <summary>
    ///     vg13 <c>eye_blurry = max(eye_blurry, amount)</c>.
    /// </summary>
    public void Blur(EntityUid uid, float amount)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp) || comp.EyeBlurry >= amount)
            return;

        comp.EyeBlurry = amount;
        Dirty(uid, comp);
        _blurry.UpdateBlurMagnitude(uid);
    }

    /// <summary>
    ///     vg13 <c>eye_blurry = min(eye_blurry + amount, max)</c>.
    /// </summary>
    public void AdjustBlur(EntityUid uid, float amount, float max = float.MaxValue)
    {
        if (!TryComp<VgStatusComponent>(uid, out var comp))
            return;

        comp.EyeBlurry = Math.Clamp(comp.EyeBlurry + amount, 0, MathF.Max(max, comp.EyeBlurry));
        Dirty(uid, comp);
        _blurry.UpdateBlurMagnitude(uid);
    }

    public float GetBlur(EntityUid uid)
    {
        return CompOrNull<VgStatusComponent>(uid)?.EyeBlurry ?? 0;
    }

    /// <summary>
    ///     vg13 handle_regular_status_updates(): halloss over 100 knocks you out, and it wears off over time.
    /// </summary>
    private void HandleHalloss(Entity<VgStatusComponent> ent)
    {
        if (ent.Comp.Halloss > 100)
        {
            Visible(ent,
                Loc.GetString("vg-pain-slump-others", ("target", Identity.Entity(ent, EntityManager))),
                Loc.GetString("vg-pain-slump-self"));
            Paralyse(ent, 10);
            ent.Comp.Halloss = 99;
        }

        if (ent.Comp.Halloss > 0)
            ent.Comp.Halloss = MathF.Max(0, ent.Comp.Halloss - (IsParalysed(ent) ? 3 : 1));
    }

    /// <summary>
    ///     vg13 update_pain_level(): how much pain the mob is in right now.
    /// </summary>
    public float GetPainLevel(EntityUid uid, VgStatusComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return 0;

        if (comp.PainNumb > 0 || _species.Get(uid)?.NoPain == true)
            return 0;

        var pain = 2 * comp.Halloss;
        if (TryComp<DamageableComponent>(uid, out var damageable))
        {
            var dict = damageable.Damage.DamageDict;
            float Get(string type) => dict.TryGetValue(type, out var value) ? value.Float() : 0;

            pain += 1 * Get(VgDamageTranslation.Oxygen) +
                    0.7f * Get(VgDamageTranslation.Toxin) +
                    1.5f * Get(VgDamageTranslation.Burn) +
                    1.2f * Get(VgDamageTranslation.Brute) +
                    1.7f * Get(VgDamageTranslation.Clone);
        }

        // Broken or ripped off limbs add a lot.
        if (TryComp<VgBodyComponent>(uid, out var body))
        {
            foreach (var limb in body.Limbs.Values)
            {
                if (!limb.IsOrganic)
                    continue;

                if (!limb.IsExisting && !limb.Amputated)
                {
                    pain += 45;
                }
                else if ((limb.Status & VgLimbStatus.Broken) != 0 || limb.Open > 0)
                {
                    pain += 30;
                    if ((limb.Status & VgLimbStatus.Splinted) != 0)
                        pain -= 25;
                }
            }
        }

        // Being drunk dulls the pain a bit.
        if (IsSlurring(uid))
            pain -= 20;

        return MathF.Max(0, pain - comp.PainResistance);
    }

    /// <summary>
    ///     vg13 human handle_shock(): staying over the pain threshold builds up traumatic shock.
    /// </summary>
    private void HandleShock(Entity<VgStatusComponent> ent, int lifeTick, System.Random random)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        var painUp = true;

        if (Health(uid) < 0)
        {
            // Going into crit makes you collapse immediately.
            comp.ShockStage = Math.Max(comp.ShockStage, 61);
        }
        else if (GetPainLevel(uid, comp) >= comp.PainResist)
        {
            comp.ShockStage++;
        }
        else
        {
            comp.ShockStage = Math.Clamp(comp.ShockStage - 1, 0, 160);
            painUp = false;
        }

        var stage = comp.ShockStage;
        var conscious = !IsParalysed(uid) && _mobState.IsAlive(uid);
        var target = Identity.Entity(uid, EntityManager);

        if (!painUp)
        {
            if (!conscious)
                return;

            if (stage == 29)
                Self(uid, Loc.GetString("vg-shock-manageable"), PopupType.Medium);
            else if (stage == 49)
                Self(uid, Loc.GetString("vg-shock-movement"), PopupType.Medium);
            else if (stage >= 50 && random.Prob(2))
                Self(uid, Loc.GetString($"vg-shock-recovering-{random.Next(1, 4)}"), PopupType.Medium);
            return;
        }

        if (stage == 10)
            Self(uid, Loc.GetString($"vg-shock-hurts-{random.Next(1, 4)}"));

        if (stage >= 30)
        {
            if (stage == 30 && conscious)
                Visible(uid, Loc.GetString("vg-shock-eyes-others", ("target", target)), Loc.GetString("vg-shock-eyes-self"));

            Blur(uid, 2);
            Stutter(uid, 5);
        }

        if (stage == 40)
            Self(uid, Loc.GetString($"vg-shock-excruciating-{random.Next(1, 4)}"));

        if (stage is >= 60 and < 80)
        {
            if (stage == 60 && conscious)
                Visible(uid, Loc.GetString("vg-shock-limp-others", ("target", target)), Loc.GetString("vg-shock-limp-self"));

            if (random.Prob(2))
            {
                Self(uid, Loc.GetString($"vg-shock-excruciating-{random.Next(1, 4)}"));
                Knockdown(uid, 10);
            }
        }

        if (stage is >= 80 and < 150 && random.Prob(5))
        {
            Self(uid, Loc.GetString($"vg-shock-excruciating-{random.Next(1, 4)}"));
            Knockdown(uid, 10);
        }

        if (stage is >= 120 and < 150 && random.Prob(2))
        {
            Self(uid, Loc.GetString($"vg-shock-blackout-{random.Next(1, 4)}"));
            Paralyse(uid, 5);
        }

        if (stage == 150)
        {
            if (conscious)
                Visible(uid, Loc.GetString("vg-shock-collapse-others", ("target", target)), Loc.GetString("vg-shock-collapse-self"));
            Knockdown(uid, 10);
        }

        if (stage >= 150 && lifeTick % 8 == 0 && random.Prob(80))
            Knockdown(uid, 4);
    }

    /// <summary>
    ///     vg13 human handle_pain(): a message about the worst-hurt limb, stumps and toxins.
    /// </summary>
    private void HandlePain(Entity<VgStatusComponent> ent, System.Random random)
    {
        if (IsParalysed(ent) || !TryComp<VgBodyComponent>(ent, out var body))
            return;

        var maxDamage = 0f;
        VgLimb? worst = null;
        foreach (var limb in body.Limbs.Values)
        {
            // Dead, metal and cleanly amputated limbs don't hurt.
            if ((limb.Status & VgLimbStatus.Dead) != 0 || !limb.IsOrganic || limb.Amputated)
                continue;

            // A shredded stump does.
            if (!limb.IsExisting)
            {
                if (random.Prob(2))
                    CustomPain((ent.Owner, ent.Comp), Loc.GetString("vg-pain-stump", ("limb", Loc.GetString(limb.Name))), ent.Owner, true);
                continue;
            }

            // Usually the worst limb, but sometimes a less damaged one.
            var damage = limb.Health;
            if (damage > maxDamage && (maxDamage == 0 || random.Prob(70)))
            {
                worst = limb;
                maxDamage = damage;
            }
        }

        if (worst != null)
            Pain(ent, Loc.GetString(worst.Name), maxDamage, false, random);

        var toxin = TryComp<DamageableComponent>(ent, out var damageable) &&
                    damageable.Damage.DamageDict.TryGetValue(VgDamageTranslation.Toxin, out var tox)
            ? tox.Float()
            : 0;

        var (key, chance) = toxin switch
        {
            >= 26 => ("vg-pain-toxin-5", 5),
            >= 15 => ("vg-pain-toxin-4", 3),
            >= 11 => ("vg-pain-toxin-3", 2),
            >= 6 => ("vg-pain-toxin-2", 2),
            >= 1 => ("vg-pain-toxin-1", 1),
            _ => (null, 0),
        };

        if (key != null && random.Prob(chance))
            CustomPain((ent.Owner, ent.Comp), Loc.GetString(key), ent.Owner, toxin >= 15);
    }

    /// <summary>
    ///     vg13 pain(): "Your left arm hurts badly." Bad enough pain can make you drop what you're holding.
    /// </summary>
    private void Pain(Entity<VgStatusComponent> ent, string limb, float amount, bool force, System.Random random)
    {
        if (_timing.CurTime < ent.Comp.NextPainTime && !force)
            return;

        if (amount > 50 && random.Prob(amount / 5) && _hands.GetActiveItem(ent.Owner) is { } held)
        {
            Self(ent, Loc.GetString("vg-pain-drop", ("limb", limb), ("item", held)));
            _hands.TryDrop(ent.Owner, checkActionBlocker: false);
        }

        var (key, type) = amount switch
        {
            < 1 => (null, PopupType.Small),
            <= 10 => ("vg-pain-hurts", PopupType.Small),
            <= 90 => ("vg-pain-hurts-badly", PopupType.MediumCaution),
            _ => ("vg-pain-hurts-terribly", PopupType.LargeCaution),
        };

        if (key != null)
        {
            var message = Loc.GetString(key, ("limb", limb));
            if (message != ent.Comp.LastPainMessage || random.Prob(10))
            {
                ent.Comp.LastPainMessage = message;
                Self(ent, message, type);
            }
        }

        ent.Comp.NextPainTime = _timing.CurTime + TimeSpan.FromSeconds((100 - amount) / 10);
    }

    /// <summary>
    ///     vg13 custom_pain(): a pain message, kept from spamming.
    /// </summary>
    /// <param name="predictingUser">Whose client predicts this, if anyone (see <see cref="VgPopupSystem.Visible"/>).</param>
    public void CustomPain(Entity<VgStatusComponent?> ent, string message, EntityUid? predictingUser, bool strong = false)
    {
        if (!Resolve(ent, ref ent.Comp, false) || IsParalysed(ent) || !_mobState.IsAlive(ent))
            return;

        if (message != ent.Comp.LastPainMessage || _timing.CurTime >= ent.Comp.NextPainTime)
        {
            ent.Comp.LastPainMessage = message;
            _popup.Self(ent, message, predictingUser, strong ? PopupType.LargeCaution : PopupType.MediumCaution);
        }

        ent.Comp.NextPainTime = _timing.CurTime + TimeSpan.FromSeconds(10);
        Dirty(ent);
    }

    /// <summary>
    ///     vg13 health: 100 minus the mob's damage.
    /// </summary>
    private float Health(EntityUid uid)
    {
        return TryComp<DamageableComponent>(uid, out var damageable) ? 100 - damageable.TotalDamage.Float() : 100;
    }

    #endregion

    // Life() tick messages are predicted by the mob's own client.
    private void Self(EntityUid uid, string message, PopupType type = PopupType.MediumCaution)
    {
        _popup.Self(uid, message, uid, type);
    }

    private void Visible(EntityUid uid, string others, string self)
    {
        _popup.Visible(uid, others, self, uid);
    }
}
