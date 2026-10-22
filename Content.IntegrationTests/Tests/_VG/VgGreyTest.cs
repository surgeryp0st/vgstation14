using System.Collections.Generic;
using Content.Server.Body.Components;
using Content.Shared._VG.Breathing;
using Content.Shared._VG.Life;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgGreyTest
{
    private static float Damage(IEntityManager entMan, EntityUid uid, string type)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type).Float();
    }

    [Test]
    public async Task WaterBurnsGreysButNotHumans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var grey = entMan.SpawnEntity("MobGrey", map.GridCoords);
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { grey, human })
            {
                entMan.RemoveComponent<VgBreathingComponent>(mob);
                entMan.System<Content.Server.Body.Systems.BloodstreamSystem>()
                    .TryAddToChemicals(mob, new Solution("Water", FixedPoint2.New(10)));
                var ev = new VgLifeTickEvent(1);
                entMan.EventBus.RaiseLocalEvent(mob, ref ev);
            }

            // REM of toxin and of burn a tick, on a Grey only (the Life() tick heals a little of the burn again)
            Assert.That(Damage(entMan, grey, "Toxin"), Is.EqualTo(0.5).Within(0.01));
            Assert.That(Damage(entMan, grey, "Burn"), Is.GreaterThan(0.1));
            Assert.That(Damage(entMan, human, "Toxin"), Is.EqualTo(0));
            Assert.That(Damage(entMan, human, "Burn"), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SplashedWaterBurnsAGrey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var grey = entMan.SpawnEntity("MobGrey", map.GridCoords);
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { grey, human })
                entMan.System<ReactiveSystem>().DoEntityReaction(mob, new Solution("Water", FixedPoint2.New(5)), ReactionMethod.Touch);

            // min(15, volume * 2): 10 burn from 5 units
            Assert.That(Damage(entMan, grey, "Burn"), Is.EqualTo(10).Within(0.5));
            Assert.That(Damage(entMan, human, "Burn"), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }
}
