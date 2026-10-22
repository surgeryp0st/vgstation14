using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Station.Systems;
using Content.Server._VG.Species;
using Content.Shared._VG.Species;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Content.Shared._VG.Life;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgPlasmamanTest
{
    [Test]
    public async Task PlasmamenIgniteInOxygenUnlessContained()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));

            var inventory = entMan.System<InventorySystem>();
            var plasmaman = entMan.SpawnEntity("MobPlasmaman", map.GridCoords);
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { plasmaman, human })
            {
                entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(mob);
            }

            void Tick(EntityUid mob)
            {
                var ev = new VgLifeTickEvent(1);
                entMan.EventBus.RaiseLocalEvent(mob, ref ev);
            }

            Tick(human);
            Assert.That(entMan.GetComponent<FlammableComponent>(human).OnFire, Is.False);

            Tick(plasmaman);
            Assert.That(entMan.GetComponent<FlammableComponent>(plasmaman).OnFire, Is.True, "bare plasmaman burns in air");

            // a suit alone isn't enough
            entMan.System<FlammableSystem>().Extinguish(plasmaman);
            Assert.That(inventory.SpawnItemInSlot(plasmaman, "outerClothing", "VgPlasmamanSuit", true), Is.True);
            Tick(plasmaman);
            Assert.That(entMan.GetComponent<FlammableComponent>(plasmaman).OnFire, Is.True, "no helmet");

            // suit and helmet put the fire out
            Assert.That(inventory.SpawnItemInSlot(plasmaman, "head", "VgPlasmamanHelmet", true), Is.True);
            Tick(plasmaman);
            Assert.That(entMan.GetComponent<FlammableComponent>(plasmaman).OnFire, Is.False, "contained");
            Tick(plasmaman);
            Assert.That(entMan.GetComponent<FlammableComponent>(plasmaman).OnFire, Is.False, "stays contained");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlasmamenSpawnContainedOnInternals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var spawning = server.System<StationSpawningSystem>();
            var internals = server.System<InternalsSystem>();
            var plasmaman = server.System<VgPlasmamanSystem>();
            var proto = server.ProtoMan;

            var checkedJobs = 0;
            foreach (var job in proto.EnumeratePrototypes<JobPrototype>())
            {
                if (job.StartingGear == null || job.JobEntity != null)
                    continue;

                // event roles with hardsuits aren't station jobs
                if (job.ID.StartsWith("ERT") || job.ID is "CBURN" or "DeathSquad" or "CentralCommandOfficial")
                    continue;

                var profile = HumanoidCharacterProfile.DefaultWithSpecies("Plasmaman");
                var mob = spawning.SpawnPlayerMob(map.GridCoords, job.ID, profile, station: null);
                var inv = server.System<Content.Shared.Inventory.InventorySystem>();
                inv.TryGetSlotEntity(mob, "outerClothing", out var o); inv.TryGetSlotEntity(mob, "head", out var h);
                Assert.That(plasmaman.IsContained(mob, out _), Is.True, $"{job.ID} is not contained: outer {(o == null ? "none" : entMan.ToPrettyString(o.Value))} head {(h == null ? "none" : entMan.ToPrettyString(h.Value))}");
                Assert.That(internals.AreInternalsWorking(mob), Is.True, $"{job.ID} has no internals");
                entMan.DeleteEntity(mob);
                checkedJobs++;
            }

            Assert.That(checkedJobs, Is.GreaterThan(20));
        });

        await pair.CleanReturnAsync();
    }
}
