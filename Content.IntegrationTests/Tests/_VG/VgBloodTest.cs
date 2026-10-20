using System.Collections.Generic;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.Breathing;
using Content.Shared._VG.Life;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgBloodTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static float Oxygen(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault("Oxygen").Float();
    }

    [Test]
    public async Task BloodLossHurts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            // The test map is a vacuum; leave breathing out of it.
            entMan.RemoveComponent<VgBreathingComponent>(human);
            var bloodstream = entMan.System<BloodstreamSystem>();
            var comp = entMan.GetComponent<BloodstreamComponent>(human);
            var body = entMan.GetComponent<VgBodyComponent>(human);

            // Down to about 300 of vg13's 560: between "bad" and "okay", so 6 oxygen damage a tick.
            FixedPoint2 maxVolume = comp.BloodMaxVolume;
            var max = maxVolume.Float();
            bloodstream.TryModifyBloodLevel(human, FixedPoint2.New(-max * (260f / 560f)), comp);
            LifeTick(entMan, human);

            Assert.Multiple(() =>
            {
                Assert.That(Oxygen(entMan, human), Is.EqualTo(6).Within(0.01));
                Assert.That(body.Pale);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrokenHeartStarvesTheBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            body.Organs[VgOrgans.Heart].Damage = 40;
            LifeTick(entMan, human);

            // Full blood pumped at 20% counts as under the survival line: 8 oxygen a tick.
            Assert.That(Oxygen(entMan, human), Is.EqualTo(8).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UpstreamBloodstreamLeavesVgBodiesAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid human = default;
        var before = 0f;

        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = entMan.System<BloodstreamSystem>();
            var comp = entMan.GetComponent<BloodstreamComponent>(human);
            bloodstream.TryModifyBloodLevel(human, FixedPoint2.New(-50), comp);
            before = bloodstream.GetBloodLevelPercentage(human, comp);
        });

        // SS14 would refill a few units of blood over these 5 seconds; vg13 regenerates 0.1 of 560 per 2 s tick.
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var bloodstream = entMan.System<BloodstreamSystem>();
            var after = bloodstream.GetBloodLevelPercentage(human, entMan.GetComponent<BloodstreamComponent>(human));
            Assert.That(after - before, Is.LessThan(0.3f / 560f + 0.0001f));
        });

        await pair.CleanReturnAsync();
    }
}
