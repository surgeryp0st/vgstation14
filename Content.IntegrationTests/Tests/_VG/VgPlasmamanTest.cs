using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
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
}
