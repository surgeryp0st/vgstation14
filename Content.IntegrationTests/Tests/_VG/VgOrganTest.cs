using System.Collections.Generic;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(SharedVgBodySystem))]
public sealed class VgOrganTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid, int tick)
    {
        var ev = new VgLifeTickEvent(tick);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static float Toxin(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault("Toxin").Float();
    }

    [Test]
    public async Task OrgansWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var damageable = entMan.System<DamageableSystem>();
            Assert.That(body.Organs, Has.Count.EqualTo(7));

            // Kidneys filter 0.5 light toxin damage every 10 ticks.
            damageable.TryChangeDamage(human, new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Poison"), 10), true);
            LifeTick(entMan, human, 10);
            Assert.That(Toxin(entMan, human), Is.EqualTo(9.5).Within(0.01));

            // Heavy toxin damage hurts the liver.
            damageable.TryChangeDamage(human, new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Poison"), 60), true);
            LifeTick(entMan, human, 20);
            Assert.That(body.Organs[VgOrgans.Liver].Damage, Is.EqualTo(2).Within(0.01));

            // Broken eyes are blind.
            body.Organs[VgOrgans.Eyes].Damage = 40;
            LifeTick(entMan, human, 21);
            Assert.That(entMan.HasComponent<TemporaryBlindnessComponent>(human));

            entMan.EventBus.RaiseLocalEvent(human, new RejuvenateEvent());
            Assert.That(body.Organs[VgOrgans.Liver].Damage, Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HardHitsReachOrgans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var damageable = entMan.System<DamageableSystem>();
            var hit = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), 12).WithVgZone(VgLimbs.Chest);

            // A 5% chance per hit: across many fresh bodies some chest organ gets hurt.
            var reached = false;
            for (var i = 0; i < 200 && !reached; i++)
            {
                var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
                damageable.TryChangeDamage(human, hit, true);
                foreach (var organ in entMan.GetComponent<VgBodyComponent>(human).Organs.Values)
                {
                    if (organ.Damage > 0)
                    {
                        Assert.That(organ.Parent, Is.EqualTo(VgLimbs.Chest));
                        Assert.That(organ.Damage, Is.EqualTo(6).Within(0.01));
                        reached = true;
                    }
                }

                entMan.DeleteEntity(human);
            }

            Assert.That(reached);
        });

        await pair.CleanReturnAsync();
    }
}
