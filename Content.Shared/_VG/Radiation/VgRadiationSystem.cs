using Content.Shared._VG.Armor;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Species;
using Content.Shared._VG.Status;
using System.Linq;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Events;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Radiation;

public sealed class VgRadiationSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly VgArmorSystem _armor = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;
    [Dependency] private readonly BlindableSystem _blindable = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    // vg13 RADDOSELIGHT .. RADDOSECRITICAL.
    private const int DoseLight = 100;
    private const int DoseMinor = 200;
    private const int DoseAdvanced = 300;
    private const int DoseCritical = 400;
    private const int DoseDeadly = 500;

    private static readonly EntProtoId NecrosisMeat = "FoodMeatHuman";

    // Salt for VgPredictedRandom.
    private const int SaltRadiation = 30;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgRadiationComponent, OnIrradiatedEvent>(OnIrradiated);
        SubscribeLocalEvent<VgRadiationComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<VgRadiationComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<VgRadiationComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Radiation = 0;
        ent.Comp.RadTick = 0;
        Dirty(ent);
    }

    /// <summary>
    ///     Radiation from a source, as vg13 <c>apply_effect(rads, IRRADIATE)</c>: the dose is cut by the mob's overall
    ///     radiation armor.
    /// </summary>
    private void OnIrradiated(Entity<VgRadiationComponent> ent, ref OnIrradiatedEvent args)
    {
        var rads = args.TotalRads;
        if (TryComp<VgBodyComponent>(ent, out var body))
        {
            var (armor, absorb) = _armor.GetAverageArmor((ent, body), VgArmorType.Rad);
            rads = MathF.Max(0, MathF.Max(0, rads - absorb) * (100 - armor) / 100);
        }

        Add((ent.Owner, ent.Comp), rads);
    }

    /// <summary>
    ///     vg13 <c>radiation += x</c> (apply_radiation with RAD_INTERNAL, or negative to treat it). Never below 0.
    /// </summary>
    /// <summary>
    ///     Sets the dose and how long it has been building, outright (tests and admin tools).
    /// </summary>
    public void SetDose(Entity<VgRadiationComponent?> ent, float radiation, int radTick)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Radiation = radiation;
        ent.Comp.RadTick = radTick;
        Dirty(ent);
    }

    public void Add(Entity<VgRadiationComponent?> ent, float rads)
    {
        if (!Resolve(ent, ref ent.Comp, false) || rads == 0)
            return;

        if (rads > 0 && _species.Get(ent) is { } species)
            rads *= species.RadMod;

        ent.Comp.Radiation = MathF.Max(0, ent.Comp.Radiation + rads);
        Dirty(ent);
    }

    /// <summary>
    ///     vg13 handle_mutations_and_radiation().
    /// </summary>
    private void OnLifeTick(Entity<VgRadiationComponent> ent, ref VgLifeTickEvent args)
    {
        if (_mobState.IsDead(ent))
            return;

        var comp = ent.Comp;
        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(ent).Id, SaltRadiation);

        // Radiation from the surroundings (radon in the air).
        var ambient = new VgAmbientRadiationEvent();
        RaiseLocalEvent(ent, ref ambient);
        Add((ent.Owner, comp), ambient.Rads);

        // vg13 RAD_ABSORB (Diona): radiation feeds and heals instead of poisoning.
        if (comp.Radiation > 0 && _species.Get(ent)?.RadAbsorb == true)
        {
            var rads = comp.Radiation / 25;
            comp.Radiation -= rads;
            comp.RadTick = 0;
            _nutrition.AddNutrition(ent, rads);
            Hurt(ent, "Brute", -rads);
            Hurt(ent, "Oxygen", -rads);
            Hurt(ent, "Toxin", -rads);
            Dirty(ent);
            return;
        }

        if (comp.Radiation > 0)
        {
            var damage = 0f;
            switch (comp.Radiation)
            {
                case < 1:
                    comp.Radiation = 0;
                    break;
                case < 50:
                    comp.Radiation--;
                    comp.RadTick++;
                    // Damage every 5 ticks.
                    if (MathF.Floor(comp.Radiation) % 5 == 0)
                        damage = 1;
                    break;
                case < 75:
                    comp.Radiation -= 2;
                    comp.RadTick += 2;
                    damage = 1;
                    if (random.Prob(5))
                    {
                        comp.Radiation -= 5;
                        _status.Knockdown(ent, 3);
                        _popup.Self(ent, Loc.GetString("vg-radiation-weak"), ent);
                    }

                    break;
                default:
                    comp.Radiation -= 3;
                    comp.RadTick += 3;
                    damage = 3;
                    break;
            }

            if (damage > 0)
                Hurt(ent, "Toxin", damage);
        }
        else
        {
            comp.RadTick = Math.Max(comp.RadTick - 3, 0);
        }

        if (comp.RadTick > 0)
            LongTermEffects(ent, random);

        Dirty(ent);
    }

    /// <summary>
    ///     The symptoms of a long exposure, which get likelier the more irradiated you are (vg13
    ///     handle_mutations_and_radiation()). Not yet: necrosis turning a limb to bone (vg13 swaps the
    ///     limb's species to Skellington, which limbs here don't have).
    /// </summary>
    private void LongTermEffects(Entity<VgRadiationComponent> ent, System.Random random)
    {
        var radiation = ent.Comp.Radiation;
        var radTick = ent.Comp.RadTick;
        var minor = MathF.Max(1, radiation / 20);
        var normal = MathF.Max(1, radiation / 50);
        var major = MathF.Max(1, radiation / 70);
        var extreme = MathF.Max(1, radiation / 100);

        // Arithrazine in the blood mostly holds the symptoms off.
        var attempt = new VgRadiationSymptomsAttemptEvent();
        RaiseLocalEvent(ent, ref attempt);
        if (attempt.Blocked && random.Prob(51 - extreme))
            return;

        TryComp<VgBodyComponent>(ent, out var body);

        if (radTick > DoseLight)
        {
            if (random.Prob(5 * normal))
            {
                var vomit = new VgVomitEvent();
                RaiseLocalEvent(ent, ref vomit);
            }

            if (random.Prob(5 * minor) && random.Prob(15))
            {
                var nosebleed = new VgNosebleedEvent();
                RaiseLocalEvent(ent, ref nosebleed);
            }

            if (random.Prob(5 * major))
                _status.AddHallucination(ent, random.Next(1, 6) * major);
        }

        if (radTick > DoseMinor && body != null && random.Prob(2 * major))
            InternalHemorrhage((ent, body), random, minor, major);

        if (radTick > DoseAdvanced && body != null && random.Prob(5 * normal))
        {
            // Organ damage to a random organ that isn't robotic or already badly hurt.
            var candidates = new List<VgOrgan>();
            foreach (var organ in body.Organs.Values)
            {
                if (organ.Robotic == 0 && organ.Damage <= 15 * normal)
                    candidates.Add(organ);
            }

            if (candidates.Count > 0)
            {
                var victim = candidates[random.Next(candidates.Count)];
                if (victim.Id == VgOrgans.Brain)
                    _body.AdjustBrainLoss((ent, body), random.Next(1, 4));
                else
                    _body.TakeOrganDamage((ent, body), victim, random.Next(1, 6) * normal);
            }
        }

        if (radTick > DoseCritical)
        {
            if (body != null && random.Prob(2 * major))
            {
                var candidates = body.Limbs.Values.Where(l => l.IsUsable && l.IsOrganic).ToList();
                if (candidates.Count > 0)
                {
                    var limb = candidates[random.Next(candidates.Count)];
                    _body.Mutate((ent, body), limb);
                    _popup.Self(ent, Loc.GetString("vg-radiation-mutate", ("limb", Loc.GetString(limb.Name))), ent, PopupType.Small);
                }
            }

            if (random.Prob(3 * normal))
            {
                Hurt(ent, "Clone", random.Next(1, 6));
                _popup.Self(ent, Loc.GetString("vg-radiation-agony-" + random.Next(7)), ent);
            }

            if (body != null && random.Prob(10 * minor))
                FadingEyes((ent, body), random);
        }

        if (radTick > DoseDeadly && body != null && random.Prob(2.5f * major))
            Necrosis((ent, body), random);
    }

    /// <summary>
    ///     A bleeding vein in a random organic limb, unless there are already enough wounds about.
    /// </summary>
    private void InternalHemorrhage(Entity<VgBodyComponent> ent, System.Random random, float minor, float major)
    {
        var limbs = ent.Comp.Limbs.Values.Where(l => l.IsOrganic).ToList();
        var wounds = limbs.Sum(l => l.Wounds.Count);
        if (limbs.Count == 0 || wounds >= 3 * minor)
            return;

        var victim = limbs[random.Next(limbs.Count)];
        if (victim.Wounds.Count >= major)
            return;

        if (random.Prob(35))
            _popup.Self(ent, Loc.GetString("vg-radiation-tear", ("limb", Loc.GetString(victim.Name))), ent);

        _body.OpenVein(ent, victim, major);
    }

    /// <summary>
    ///     The eyes cloud over: damage and blur, then blindness once they break, or a bout of nearsightedness while
    ///     they're bruised.
    /// </summary>
    private void FadingEyes(Entity<VgBodyComponent> ent, System.Random random)
    {
        if (!_body.TryGetOrgan((ent, ent.Comp), VgOrgans.Eyes, out var eyes) || eyes.Robotic != 0)
            return;

        var blindable = CompOrNull<BlindableComponent>(ent);
        var radiation = Comp<VgRadiationComponent>(ent);
        var now = _timing.CurTime;
        if (blindable is { IsBlind: true } || now < radiation.NearsightedUntil)
            return;

        _popup.Self(ent, Loc.GetString("vg-radiation-eyes-" + random.Next(3)), ent);
        _body.TakeOrganDamage(ent, eyes, 2.5f, silent: true);
        _status.AdjustBlur(ent, 1.5f, 50);

        if (eyes.IsBroken)
        {
            _popup.Self(ent, Loc.GetString("vg-radiation-go-blind"), ent);
            if (blindable != null)
                _blindable.SetMinDamage((ent, blindable), blindable.MaxDamage);
        }
        else if (eyes.IsBruised)
        {
            _popup.Self(ent, Loc.GetString("vg-radiation-hard-to-see"), ent);
            _status.Blind(ent, 5);
            _status.AdjustBlur(ent, 5);
            radiation.NearsightedUntil = now + TimeSpan.FromSeconds(10);
            Dirty(ent, radiation);
        }
    }

    /// <summary>
    ///     Flesh gives out: a chunk of meat falls off, or a limb does.
    /// </summary>
    private void Necrosis(Entity<VgBodyComponent> ent, System.Random random)
    {
        switch (random.Next(3))
        {
            case 0:
                var nutriment = _nutrition.GetNutrition(ent) / 5;
                if (nutriment <= 1)
                    break;

                _popup.Self(ent, Loc.GetString("vg-radiation-meat"), ent);
                // No predicted spawning in this engine version.
                if (_net.IsServer)
                {
                    var meat = Spawn(NecrosisMeat, Transform(ent).Coordinates);
                    if (_solutions.TryGetSolution(meat, "food", out var food))
                        _solutions.TryAddReagent(food.Value, "Nutriment", nutriment);
                }
                break;
            case 1:
                var limbs = ent.Comp.Limbs.Values.Where(l => l.IsOrganic && !l.Vital && l.IsExisting && !l.Amputated).ToList();
                if (limbs.Count > 0)
                    _body.DropLimb(ent, limbs[random.Next(limbs.Count)], force: true);
                break;
            default:
                // TODO VG: skeletify() a limb, once limbs can have their own species.
                break;
        }
    }

    private void Hurt(EntityUid uid, string type, float amount)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict[type] = amount;
        _damageable.TryChangeDamage(uid, spec, true, false);
    }
}

/// <summary>
///     Raised on a mob each Life() tick to collect the radiation its surroundings give it (vg13 radon_effects()).
/// </summary>
[ByRefEvent]
public record struct VgAmbientRadiationEvent(float Rads);
