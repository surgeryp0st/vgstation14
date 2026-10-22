#nullable enable
using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared._VG.Radiation;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Eye.Blinding.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 handle_mutations_and_radiation(): the long-term symptoms of a big dose.</summary>
[TestFixture]
public sealed class VgRadiationTest
{
    [Test]
    public async Task LongExposureSymptoms()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var radiation = entMan.System<VgRadiationSystem>();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(human);
        });

        var startingMeat = 0;
        var mutated = false;
        var veins = false;
        for (var i = 0; i < 40; i++)
        {
            await server.WaitAssertion(() =>
            {
                // A huge, long dose: every symptom is likely each tick. Keep them alive through it.
                radiation.SetDose(human, 1000, 600);
                entMan.System<DamageableSystem>().SetAllDamage(human, entMan.GetComponent<DamageableComponent>(human), 0);
                var ev = new VgLifeTickEvent(i);
                entMan.EventBus.RaiseLocalEvent(human, ref ev);

                var body = entMan.GetComponent<VgBodyComponent>(human);
                mutated |= body.Limbs.Values.Any(l => (l.Status & VgLimbStatus.Mutated) != 0);
                veins |= body.Limbs.Values.Any(l => l.InternallyBleeding);
            });
            await server.WaitRunTicks(1);
        }

        await server.WaitAssertion(() =>
        {
            var body = entMan.GetComponent<VgBodyComponent>(human);
            Assert.Multiple(() =>
            {
                Assert.That(veins, "internal bleeding");
                Assert.That(mutated, "a limb mutated");
                Assert.That(body.Organs[VgOrgans.Eyes].IsBroken, "the eyes broke");
                Assert.That(entMan.GetComponent<BlindableComponent>(human).IsBlind, "and went blind");
                var meat = entMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "FoodMeatHuman");
                var lostLimb = body.Limbs.Values.Any(l => !l.IsExisting);
                Assert.That(meat > startingMeat || lostLimb, "necrosis took some flesh or a limb");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArithrazineHoldsSymptomsOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var treated = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var untreated = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = entMan.GetComponent<BloodstreamComponent>(treated);
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.ResolveSolution(treated, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out _));
            solutions.TryAddReagent(bloodstream.ChemicalSolution!.Value, "Arithrazine", 5, out _);

            var withDrug = new VgRadiationSymptomsAttemptEvent();
            entMan.EventBus.RaiseLocalEvent(treated, ref withDrug);
            var without = new VgRadiationSymptomsAttemptEvent();
            entMan.EventBus.RaiseLocalEvent(untreated, ref without);

            Assert.Multiple(() =>
            {
                Assert.That(withDrug.Blocked, "arithrazine in the blood");
                Assert.That(without.Blocked, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
