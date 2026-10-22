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

    [Test]
    public async Task Vg13OnlySpeciesSpawnWithTheirNumbers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var species = entMan.System<VgSpeciesSystem>();
            foreach (var id in new[] { "Tajaran", "Skrell", "Grey", "Insectoid", "Plasmaman", "Mushroom" })
            {
                var mob = entMan.SpawnEntity("Mob" + id, map.GridCoords);
                Assert.That(species.Get(mob)?.ID, Is.EqualTo(id));
            }

            var plasmaman = entMan.SpawnEntity("MobPlasmaman", map.GridCoords);
            var breathing = entMan.GetComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(plasmaman);
            Assert.Multiple(() =>
            {
                Assert.That(breathing.BreathGas, Is.EqualTo(Content.Shared.Atmos.Gas.Plasma));
                Assert.That(breathing.ToxicGases, Is.Empty);
                Assert.That(entMan.GetComponent<VgBodyComponent>(plasmaman).BruteModifier, Is.EqualTo(1.5f));
            });

            var mushroom = entMan.SpawnEntity("MobMushroom", map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(mushroom), Is.False);
                Assert.That(entMan.GetComponent<VgBodyComponent>(mushroom).Organs.Keys, Is.EquivalentTo(new[] { "brain", "eyes" }));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeciesDamageModsApply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var slime = entMan.SpawnEntity("MobSlimePerson", map.GridCoords);
            var spec = new Content.Shared.Damage.DamageSpecifier();
            spec.DamageDict["Toxin"] = 5;
            entMan.System<Content.Shared.Damage.DamageableSystem>().TryChangeDamage(slime, spec, true, false);

            var dict = entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(slime).Damage.DamageDict;
            Assert.That(dict["Toxin"].Float(), Is.EqualTo(10).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SS14OnlySpeciesAreNotSelectable()
    {
        // Decided: SS14's Arachnid, Moth, Dwarf and Gingerbread are disabled; profiles with them fall back to Human.
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var disabled = new[] { "Arachnid", "Moth", "Dwarf", "Gingerbread" };
            Assert.Multiple(() =>
            {
                foreach (var id in disabled)
                {
                    var proto = server.ProtoMan.Index<Content.Shared.Humanoid.Prototypes.SpeciesPrototype>(id);
                    Assert.That(proto.RoundStart, Is.False, $"{id} is selectable");
                }

                for (var i = 0; i < 200; i++)
                {
                    var profile = Content.Shared.Preferences.HumanoidCharacterProfile.Random();
                    Assert.That(disabled, Does.Not.Contain(profile.Species.Id), "random character");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
