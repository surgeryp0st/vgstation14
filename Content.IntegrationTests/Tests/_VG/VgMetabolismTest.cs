using System.Collections.Generic;
using Content.Server.Body.Components;
using Content.Shared._VG.Breathing;
using Content.Shared._VG.Chemistry;
using Content.Shared._VG.Life;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgMetabolismTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static float Damage(IEntityManager entMan, EntityUid uid, string type)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type).Float();
    }

    private static float Amount(IEntityManager entMan, EntityUid uid, string reagent)
    {
        var comp = entMan.GetComponent<BloodstreamComponent>(uid);
        entMan.System<SharedSolutionContainerSystem>()
            .ResolveSolution(uid, comp.ChemicalSolutionName, ref comp.ChemicalSolution, out var solution);
        return solution!.GetTotalPrototypeQuantity(reagent).Float();
    }

    private static void Inject(IEntityManager entMan, EntityUid uid, string reagent, float amount)
    {
        entMan.System<Content.Server.Body.Systems.BloodstreamSystem>()
            .TryAddToChemicals(uid, new Content.Shared.Chemistry.Components.Solution(reagent, FixedPoint2.New(amount)));
    }

    [Test]
    public async Task DyloveneHealsToxinAndMetabolises()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);

            var spec = new DamageSpecifier();
            spec.DamageDict["Toxin"] = 10;
            entMan.System<DamageableSystem>().TryChangeDamage(human, spec, true, false);
            Inject(entMan, human, "anti_toxin", 10);
            LifeTick(entMan, human);

            Assert.Multiple(() =>
            {
                // heals 2 * REM and burns 0.2 of the 10 units
                Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(9).Within(0.01));
                Assert.That(Amount(entMan, human, "anti_toxin"), Is.EqualTo(9.8).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FluorineHurtsLikeVg13()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);

            Inject(entMan, human, "Fluorine", 5);
            LifeTick(entMan, human);

            // adjustToxLoss(REM)
            Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(0.5).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpaceDrugsMakeYouSeeThings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);

            Inject(entMan, human, "SpaceDrugs", 5);
            LifeTick(entMan, human);

            Assert.That(entMan.HasComponent<Content.Shared.Drugs.SeeingRainbowsComponent>(human));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadiationDoseHurtsAndHyronalinTreatsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);
            var rad = entMan.System<Content.Shared._VG.Radiation.VgRadiationSystem>();
            var comp = entMan.GetComponent<Content.Shared._VG.Radiation.VgRadiationComponent>(human);

            rad.Add(human, 100);
            LifeTick(entMan, human);
            // 75+ radiation: loses 3 and does 3 toxin damage.
            Assert.Multiple(() =>
            {
                Assert.That(comp.Radiation, Is.EqualTo(97).Within(0.01));
                Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(3).Within(0.01));
            });

            Inject(entMan, human, "Hyronalin", 5);
            LifeTick(entMan, human);
            // hyronalin takes 3 * REM off first, then the dose burns 3 more
            Assert.That(comp.Radiation, Is.EqualTo(97 - 1.5f - 3).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OxygenPoisonsVoxButNotHumans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var vox = entMan.SpawnEntity("MobVox", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);
            entMan.RemoveComponent<VgBreathingComponent>(vox);

            Inject(entMan, human, "Oxygen", 5);
            Inject(entMan, vox, "Oxygen", 5);
            LifeTick(entMan, human);
            LifeTick(entMan, vox);

            Assert.Multiple(() =>
            {
                Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(0).Within(0.01));
                Assert.That(Damage(entMan, vox, "Toxin"), Is.EqualTo(0.5).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BicaridineOverdoseHurts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);

            Inject(entMan, human, "bicaridine", 31);
            LifeTick(entMan, human);

            Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(1).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CryoxadoneNeedsCold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);
            var temperature = entMan.System<Content.Server.Temperature.Systems.TemperatureSystem>();

            var spec = new DamageSpecifier();
            spec.DamageDict["Toxin"] = 10;
            entMan.System<DamageableSystem>().TryChangeDamage(human, spec, true, false);
            Inject(entMan, human, "cryoxadone", 10);

            LifeTick(entMan, human);
            Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(10).Within(0.01), "warm body shouldn't heal");

            temperature.ForceChangeTemperature(human, 90);
            LifeTick(entMan, human);
            Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(8).Within(0.01), "below 95 K heals double");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PainkillersDullPain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);
            var status = entMan.System<Content.Shared._VG.Status.VgStatusSystem>();

            var spec = new DamageSpecifier();
            spec.DamageDict["Oxygen"] = 50;
            entMan.System<DamageableSystem>().TryChangeDamage(human, spec, true, false);
            Assert.That(status.GetPainLevel(human), Is.EqualTo(50).Within(0.01));

            Inject(entMan, human, "paracetamol", 10);
            LifeTick(entMan, human);
            Assert.That(status.GetPainLevel(human), Is.EqualTo(0).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }
}
