using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgStatusSystem))]
public sealed class VgStatusTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid, int tick = 1)
    {
        var ev = new VgLifeTickEvent(tick);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    [Test]
    public async Task StaminaBecomesHalloss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<StaminaSystem>().TakeStaminaDamage(human, 30);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<VgStatusComponent>(human).Halloss, Is.EqualTo(30).Within(0.01));
                Assert.That(entMan.GetComponent<StaminaComponent>(human).StaminaDamage, Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BatonStunsForTwentySeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var baton = entMan.SpawnEntity("Stunbaton", map.GridCoords);
            entMan.System<StaminaSystem>().TakeStaminaDamage(human, 35, with: baton);

            var effects = entMan.System<StatusEffectsSystem>();
            var now = timing.CurTime;
            Assert.Multiple(() =>
            {
                Assert.That(effects.TryGetTime(human, "Stun", out var stun));
                Assert.That(stun!.Value.Item2 - now, Is.EqualTo(TimeSpan.FromSeconds(20)).Within(TimeSpan.FromSeconds(0.1)));
                Assert.That(entMan.HasComponent<KnockedDownComponent>(human));
                // The baton doesn't add halloss in vg13.
                Assert.That(entMan.GetComponent<VgStatusComponent>(human).Halloss, Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TooMuchHallossKnocksOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var status = entMan.System<VgStatusSystem>();
            status.AdjustHalloss(human, 110);
            LifeTick(entMan, human);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ForcedSleepingComponent>(human));
                // Set to 99, then 3 wears off while out cold.
                Assert.That(entMan.GetComponent<VgStatusComponent>(human).Halloss, Is.EqualTo(96).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PainBuildsShock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.GetComponent<VgStatusComponent>(human);

            // 70 brute is 84 pain, over the 80 threshold.
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 70);
            entMan.System<DamageableSystem>().TryChangeDamage(human, damage, true);

            for (var i = 1; i <= 5; i++)
            {
                LifeTick(entMan, human, i);
            }

            Assert.That(comp.ShockStage, Is.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TaserElectrode()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entMan.SpawnEntity("BulletTaser", map.GridCoords);
            var ev = new ProjectileHitEvent(new DamageSpecifier(), human);
            entMan.EventBus.RaiseLocalEvent(bullet, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<StunnedComponent>(human));
                Assert.That(entMan.HasComponent<KnockedDownComponent>(human));
                Assert.That(entMan.GetComponent<VgStatusComponent>(human).Halloss, Is.EqualTo(10).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }
}
