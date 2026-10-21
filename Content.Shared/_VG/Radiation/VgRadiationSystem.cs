using Content.Shared._VG.Armor;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared._VG.Species;
using Content.Shared._VG.Status;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Events;
using Content.Shared.Rejuvenate;
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

    // vg13 RADDOSELIGHT .. RADDOSECRITICAL.
    private const int DoseLight = 100;
    private const int DoseMinor = 200;
    private const int DoseAdvanced = 300;
    private const int DoseCritical = 400;

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
    ///     The symptoms of a long exposure, which get likelier the more irradiated you are. Nosebleeds,
    ///     hallucinations, internal bleeding, limb mutation, blindness and necrosis wait for the systems they need.
    /// </summary>
    private void LongTermEffects(Entity<VgRadiationComponent> ent, System.Random random)
    {
        var radiation = ent.Comp.Radiation;
        var radTick = ent.Comp.RadTick;
        var normal = MathF.Max(1, radiation / 50);

        if (radTick > DoseLight && random.Prob(5 * normal))
        {
            var vomit = new VgVomitEvent();
            RaiseLocalEvent(ent, ref vomit);
        }

        if (radTick > DoseAdvanced && TryComp<VgBodyComponent>(ent, out var body) && random.Prob(5 * normal))
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

        if (radTick > DoseCritical && random.Prob(3 * normal))
        {
            Hurt(ent, "Clone", random.Next(1, 6));
            _popup.Self(ent, Loc.GetString("vg-radiation-agony-" + random.Next(7)), ent);
        }
    }

    private void Hurt(EntityUid uid, string type, float amount)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict[type] = amount;
        _damageable.TryChangeDamage(uid, spec, true, false);
    }
}
