#nullable enable
using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared.Chemistry.Components;
using Content.Shared.Flash.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 hallucinations: the counter, what causes and treats it, and what it does.</summary>
[TestFixture]
public sealed class VgHallucinationTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static void Inject(IEntityManager entMan, EntityUid uid, string reagent, float amount)
    {
        entMan.System<Content.Server.Body.Systems.BloodstreamSystem>().TryAddToChemicals(uid, new Solution(reagent, FixedPoint2.New(amount)));
    }

    [Test]
    public async Task HallucinationComesAndGoes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = entMan.System<VgStatusSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(human);

            Assert.Multiple(() =>
            {
                // vg13: two a Life() tick, and when it's gone the imagined pain goes with it.
                status.AddHallucination(human, 10);
                status.AdjustHalloss(human, 30);
                LifeTick(entMan, human);
                Assert.That(status.GetHallucination(human), Is.EqualTo(8));
                status.AddHallucination(human, -6);
                LifeTick(entMan, human);
                Assert.That(status.GetHallucination(human), Is.Zero);
                Assert.That(status.GetHalloss(human), Is.Zero, "halloss goes with it");

                // Mindbreaker: 10 a tick.
                Inject(entMan, human, "MindbreakerToxin", 5);
                LifeTick(entMan, human);
                Assert.That(status.GetHallucination(human), Is.GreaterThanOrEqualTo(8));
            });

            // Dylovene takes it away, twice as fast lying down.
            var standing = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var lying = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<StandingStateSystem>().Down(lying);
            foreach (var mob in new[] { standing, lying })
            {
                entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(mob);
                status.AddHallucination(mob, 100);
                Inject(entMan, mob, "anti_toxin", 10);
                LifeTick(entMan, mob);
            }

            // 100 - 2 (wearing off) - 5 * REM (0.5) * lucidity.
            Assert.Multiple(() =>
            {
                Assert.That(status.GetHallucination(standing), Is.EqualTo(100 - 2 - 2.5f).Within(0.01));
                Assert.That(status.GetHallucination(lying), Is.EqualTo(100 - 2 - 5f).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryHallucinationRuns()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var hallucination = entMan.System<Content.Server._VG.Status.VgHallucinationSystem>();
            var status = entMan.System<VgStatusSystem>();
            var human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 1.5f, 0.5f));
            entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 3.5f, 0.5f));

            // Every pick of vg13's switch(rand(1, 106)) runs.
            for (var pick = 1; pick <= 106; pick++)
            {
                Assert.DoesNotThrow(() => hallucination.Hallucinate(human, pick), $"pick {pick}");
            }

            // Fake death really puts you to sleep; a fake flash really flashes you.
            var sleeper = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 2.5f, 0.5f));
            hallucination.Hallucinate(sleeper, 71);
            Assert.That(status.IsAsleep(sleeper), "fake death sleeps");
            var flashed = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 2.5f, 0.5f));
            hallucination.Hallucinate(flashed, 81);
            Assert.That(entMan.HasComponent<FlashedComponent>(flashed), "fake flash flashes");
        });

        // A badly hallucinating mob gets hallucinations, and a fake attacker now and then, without breaking.
        await server.WaitPost(() =>
        {
            var heavy = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 0.5f, 0.5f));
            entMan.System<VgStatusSystem>().AddHallucination(heavy, 400);
        });
        await server.WaitRunTicks(300);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientDrawsHallucinations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEntMan = server.EntMan;

        EntityUid human = default;
        await server.WaitPost(() => human = sEntMan.SpawnEntity("MobHuman", map.GridCoords));
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var cEntMan = client.EntMan;
            var where = sEntMan.GetNetCoordinates(map.GridCoords);
            int Images() => cEntMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "VgHallucinationImage");

            void Raise(VgHallucinationEvent ev) => cEntMan.EventBus.RaiseEvent(EventSource.Network, ev);

            var before = Images();
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.DangerFlash, Where = where, Rsi = "/Textures/_VG/turf/space.rsi", State = "3", Duration = 0.1f });
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.DangerFlash, Where = where, Prototype = "C4", Duration = 0.1f });
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.Disguise, Target = sEntMan.GetNetEntity(human), Prototype = "FoodApple", Duration = 0.1f });
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.AttackerMove, Id = 1, Where = where, Target = sEntMan.GetNetEntity(human) });
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.Sound, Sound = "/Audio/_VG/Hallucination/growl1.ogg" });
            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.StrangeItem, Prototype = "Emag", Duration = 0.1f });
            Assert.That(Images() - before, Is.EqualTo(4), "two flashes, a disguise and an attacker");

            Raise(new VgHallucinationEvent { Kind = VgHallucinationKind.AttackerGone, Id = 1 });
        });

        // They all go again.
        await pair.RunTicksSync(30);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "VgHallucinationImage"), Is.Zero);
        });

        await pair.CleanReturnAsync();
    }
}
