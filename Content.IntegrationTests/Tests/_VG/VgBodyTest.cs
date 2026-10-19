using System.Linq;
using Content.Server._VG.Body;
using Content.Server._VG.Life;
using Content.Shared._VG.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgBodySystem))]
public sealed class VgBodyTest
{
    private static DamageSpecifier Blunt(IPrototypeManager protoMan, float amount)
    {
        return new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), amount);
    }

    private static float LimbTotal(VgBodyComponent body, Func<VgLimb, float> damage)
    {
        return body.Limbs.Values.Where(l => l.IsOrganic && l.IsExisting).Sum(damage);
    }

    [Test]
    public async Task TargetedDamageHitsLimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var damage = entMan.GetComponent<DamageableComponent>(human);

            Assert.That(body.Limbs, Has.Count.EqualTo(11));

            damageable.TryChangeDamage(human, Blunt(protoMan, 10).WithVgZone(VgLimbs.LeftArm), true);

            var arm = body.Limbs[VgLimbs.LeftArm];
            Assert.Multiple(() =>
            {
                Assert.That(arm.BruteDamage, Is.EqualTo(10).Within(0.01));
                Assert.That(arm.Wounds, Is.Not.Empty);
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(10).Within(0.01));
                Assert.That(damage.Damage.DamageDict.ContainsKey("Blunt"), Is.False);
            });

            // Healing comes off the limbs, and the totals follow.
            damageable.TryChangeDamage(human, Blunt(protoMan, -10), true);
            Assert.Multiple(() =>
            {
                Assert.That(arm.BruteDamage, Is.EqualTo(0).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(0).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UntargetedDamageMatchesLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var damage = entMan.GetComponent<DamageableComponent>(human);

            var burn = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Heat"), 25);
            damageable.TryChangeDamage(human, burn + Blunt(protoMan, 15), true);

            Assert.Multiple(() =>
            {
                Assert.That(LimbTotal(body, l => l.BruteDamage), Is.EqualTo(15).Within(0.01));
                Assert.That(LimbTotal(body, l => l.BurnDamage), Is.EqualTo(25).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(15).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Burn"].Float(), Is.EqualTo(25).Within(0.01));
            });

            // Admin heal / rejuvenate clears everything.
            entMan.EventBus.RaiseLocalEvent(human, new RejuvenateEvent());
            Assert.Multiple(() =>
            {
                Assert.That(body.Limbs.Values.All(l => l.Wounds.Count == 0 && l.Health == 0));
                Assert.That(damage.TotalDamage.Float(), Is.EqualTo(0).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LimbFracturesOnLifeTick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);

            // An arm breaks above 30 brute.
            damageable.TryChangeDamage(human, Blunt(protoMan, 35).WithVgZone(VgLimbs.LeftArm), true);
            Assert.That(body.Limbs[VgLimbs.LeftArm].IsBroken, Is.False);

            var tick = new VgLifeTickEvent(1);
            entMan.EventBus.RaiseLocalEvent(human, ref tick);
            Assert.That(body.Limbs[VgLimbs.LeftArm].IsBroken, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharpEnoughHitSeversLimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();
        var mobState = entMan.System<MobStateSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var hands = entMan.GetComponent<HandsComponent>(human);
            var handsBefore = hands.Count;

            // vg13 treats sharpness 100 as always able to sever.
            var slash = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Slash"), 10) { VgSharpness = 100 };
            damageable.TryChangeDamage(human, slash.WithVgZone(VgLimbs.RightArm), true);

            var arm = body.Limbs[VgLimbs.RightArm];
            var hand = body.Limbs[VgLimbs.RightHand];
            Assert.Multiple(() =>
            {
                Assert.That(arm.IsExisting, Is.False);
                Assert.That(hand.IsExisting, Is.False);
                Assert.That(hand.Amputated, Is.True);
                Assert.That(mobState.IsAlive(human), Is.True);
            });
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var human = entMan.EntityQuery<VgBodyComponent>().Select(b => b.Owner).First();
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<HandsComponent>(human).Count, Is.EqualTo(1));
                Assert.That(entMan.EntityQuery<MetaDataComponent>().Any(m => m.EntityPrototype?.ID == "VgLimbRightArm"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
