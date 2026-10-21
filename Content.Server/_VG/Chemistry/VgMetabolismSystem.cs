using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._VG.Body;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Chemistry;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VG.Chemistry;

/// <summary>
///     vg13 <c>reagents.metabolize()</c> on the bloodstream's chemical solution, once per Life() tick. Server-side,
///     since the bloodstream is.
/// </summary>
public sealed class VgMetabolismSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private readonly List<string> _present = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgMetabolismComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<TemperatureComponent, VgGetBodyTemperatureEvent>(OnGetTemperature);
    }

    private void OnGetTemperature(Entity<TemperatureComponent> ent, ref VgGetBodyTemperatureEvent args)
    {
        args.Temperature = ent.Comp.CurrentTemperature;
    }

    private void OnLifeTick(Entity<VgMetabolismComponent> ent, ref VgLifeTickEvent args)
    {
        if (!TryComp<BloodstreamComponent>(ent, out var bloodstream) ||
            !_solutions.ResolveSolution(ent.Owner, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals))
        {
            return;
        }

        // Dead mobs don't process anything, as the Life() tick stops for them in vg13.
        if (_mobState.IsDead(ent))
            return;

        var efficiency = LiverEfficiency(ent);
        var painResistance = 0f;

        _present.Clear();
        foreach (var quantity in chemicals.Contents.ToArray())
        {
            var id = quantity.Reagent.Prototype;
            _present.Add(id);

            ent.Comp.Ticks.TryGetValue(id, out var tick);
            tick++;
            ent.Comp.Ticks[id] = tick;

            var metabolism = VgReagentPrototype.DefaultMetabolism;
            if (_proto.TryIndex<VgReagentPrototype>(id, out var vg))
            {
                metabolism = vg.Metabolism;
                painResistance += vg.PainResistance;
                if (vg.NutrimentFactor != 0)
                    _nutrition.AddNutrition(ent, vg.NutrimentFactor);
                var volume = quantity.Quantity;
                var args2 = new VgReagentArgs(EntityManager, ent, id, volume, tick, (reagent, amount) =>
                    _solutions.RemoveReagent(bloodstream.ChemicalSolution!.Value, reagent, FixedPoint2.New(amount)));

                foreach (var effect in vg.Effects)
                {
                    if (effect.InTickRange(tick) && (effect.Chance >= 100 || _random.Prob(effect.Chance / 100)))
                        effect.Apply(args2);
                }

                var overdosing = vg.OverdoseAmount > 0 && volume.Float() >= vg.OverdoseAmount ||
                                 vg.OverdoseTick > 0 && tick >= vg.OverdoseTick;
                if (overdosing)
                {
                    // vg13 on_overdose() defaults to adjustToxLoss(1).
                    if (vg.OverdoseEffects.Count == 0)
                        new VgAdjustDamage { Type = "Toxin", Amount = 1, Flat = true }.Apply(args2);

                    foreach (var effect in vg.OverdoseEffects)
                    {
                        if (effect.Chance >= 100 || _random.Prob(effect.Chance / 100))
                            effect.Apply(args2);
                    }
                }
            }

            // vg13 liver.metabolize_reagent(): metabolism * efficiency.
            _solutions.RemoveReagent(bloodstream.ChemicalSolution!.Value, id, FixedPoint2.New(metabolism * efficiency));
        }

        _status.SetPainResistance(ent, painResistance);

        // Reagents that left the body forget how long they were in it.
        if (ent.Comp.Ticks.Count > _present.Count)
        {
            foreach (var id in ent.Comp.Ticks.Keys.ToArray())
            {
                if (!_present.Contains(id))
                    ent.Comp.Ticks.Remove(id);
            }
        }
    }

    /// <summary>
    ///     vg13 liver <c>efficiency</c>. Bodies without a liver (non-humans in vg13) metabolise at the base rate.
    /// </summary>
    private float LiverEfficiency(EntityUid uid)
    {
        // TODO VG: species liver efficiencies and per-reagent reagent_efficiencies; vg13's default liver is 1.
        return 1;
    }
}
