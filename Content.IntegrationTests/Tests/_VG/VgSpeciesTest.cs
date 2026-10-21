using Content.Shared._VG.Body;
using Content.Shared._VG.Species;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgSpeciesTest
{
    [Test]
    public async Task SpeciesNumbersAreReadFromThePrototype()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var species = entMan.System<VgSpeciesSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var unathi = entMan.SpawnEntity("MobReptilian", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(species.Get(human)!.ID, Is.EqualTo("Human"));
                Assert.That(species.Get(human)!.PunchDamage, Is.EqualTo(0));
                Assert.That(species.Get(unathi)!.ID, Is.EqualTo("Unathi"));
                Assert.That(species.Get(unathi)!.Claws);
                Assert.That(entMan.GetComponent<VgBodyComponent>(unathi).Organs, Has.Count.EqualTo(7));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VoxBreatheNitrogenAndOxygenPoisonsThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var vox = entMan.SpawnEntity("MobVox", map.GridCoords);
            var breathing = entMan.GetComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(vox);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.System<VgSpeciesSystem>().Get(vox)!.ID, Is.EqualTo("Vox"));
                Assert.That(breathing.BreathGas, Is.EqualTo(Content.Shared.Atmos.Gas.Nitrogen));
                Assert.That(breathing.ToxicGases, Has.Count.EqualTo(2));
                Assert.That(breathing.ToxicGases[0].Gas, Is.EqualTo(Content.Shared.Atmos.Gas.Oxygen));
            });
        });

        await pair.CleanReturnAsync();
    }
}
