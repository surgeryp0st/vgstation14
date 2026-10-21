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

    [Test]
    public async Task SpeciesFlagsRemoveWhatTheyDontHave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var skeleton = entMan.SpawnEntity("MobSkeletonPerson", map.GridCoords);
            var diona = entMan.SpawnEntity("MobDiona", map.GridCoords);
            var slime = entMan.SpawnEntity("MobSlimePerson", map.GridCoords);

            foreach (var mob in new[] { skeleton, diona, slime })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.HasComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(mob), Is.False);
                    Assert.That(entMan.GetComponent<VgBodyComponent>(mob).HasBlood, Is.False);
                });
            }

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<VgBodyComponent>(skeleton).Organs.Keys, Is.EquivalentTo(new[] { "brain" }));
                Assert.That(entMan.GetComponent<VgBodyComponent>(slime).Organs.Keys, Is.EquivalentTo(new[] { "brain" }));
                Assert.That(entMan.GetComponent<VgBodyComponent>(diona).Organs, Has.Count.EqualTo(7));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DionaAbsorbRadiation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var diona = entMan.SpawnEntity("MobDiona", map.GridCoords);
            var rad = entMan.System<Content.Shared._VG.Radiation.VgRadiationSystem>();
            var comp = entMan.GetComponent<Content.Shared._VG.Radiation.VgRadiationComponent>(diona);
            var nutrition = entMan.GetComponent<Content.Shared._VG.Nutrition.VgNutritionComponent>(diona);

            rad.Add(diona, 100);
            var before = nutrition.Nutrition;
            var ev = new Content.Shared._VG.Life.VgLifeTickEvent(1);
            entMan.EventBus.RaiseLocalEvent(diona, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Radiation, Is.EqualTo(96).Within(0.01));
                Assert.That(nutrition.Nutrition, Is.GreaterThan(before));
            });
        });

        await pair.CleanReturnAsync();
    }
}
