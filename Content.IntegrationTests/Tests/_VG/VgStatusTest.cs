using System.Linq;
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

    [Test]
    public async Task EyeBlurryWearsOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = entMan.System<VgStatusSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var blindfolded = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var blinded = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = entMan.System<Content.Shared.Inventory.InventorySystem>();
            Assert.That(inventory.SpawnItemInSlot(blindfolded, "eyes", "ClothingEyesBlindfold", true, true));

            Assert.Multiple(() =>
            {
                // eye_blurry = max(eye_blurry, n) and min(eye_blurry + n, cap).
                status.Blur(human, 5);
                status.Blur(human, 2);
                Assert.That(status.GetBlur(human), Is.EqualTo(5));
                status.AdjustBlur(human, 10, 8);
                Assert.That(status.GetBlur(human), Is.EqualTo(8));

                // SS14's blur overlay shows it.
                Assert.That(entMan.TryGetComponent<Content.Shared.Eye.Blinding.Components.BlurryVisionComponent>(human, out var blur));
                Assert.That(blur!.Magnitude, Is.GreaterThan(0));

                LifeTick(entMan, human);
                Assert.That(status.GetBlur(human), Is.EqualTo(7), "one a tick");

                status.Blur(blindfolded, 8);
                LifeTick(entMan, blindfolded);
                Assert.That(status.GetBlur(blindfolded), Is.EqualTo(5), "three a tick under a blindfold");

                status.Blur(blinded, 8);
                status.Blind(blinded, 5);
                LifeTick(entMan, blinded);
                Assert.That(status.GetBlur(blinded), Is.EqualTo(8), "held while blind");

                status.AdjustBlur(human, -100);
                Assert.That(status.GetBlur(human), Is.Zero);
            });
        });

        // The overlay goes once the blur has.
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var human = entMan.EntityQuery<VgStatusComponent>().Select(c => c.Owner).First(u => entMan.System<VgStatusSystem>().GetBlur(u) == 0
                && !entMan.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(u, "eyes", out _));
            Assert.That(entMan.HasComponent<Content.Shared.Eye.Blinding.Components.BlurryVisionComponent>(human), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlurringIsDrunkSpeech()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = entMan.System<VgStatusSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            status.AdjustHalloss(human, 50);
            var sober = status.GetPainLevel(human);

            status.Slur(human, 5);
            Assert.Multiple(() =>
            {
                Assert.That(status.IsSlurring(human));
                Assert.That(entMan.HasComponent<Content.Server.Speech.Components.SlurredAccentComponent>(human), "SS14's slurred accent");
                // vg13 shock: being drunk dulls the pain by 20.
                Assert.That(status.GetPainLevel(human), Is.EqualTo(sober - 20).Within(0.01f));
            });

            // max(slurring, n): a shorter slur doesn't cut a longer one short.
            entMan.System<StatusEffectsSystem>().TryGetTime(human, "SlurredSpeech", out var before);
            status.Slur(human, 1);
            entMan.System<StatusEffectsSystem>().TryGetTime(human, "SlurredSpeech", out var after);
            Assert.That(after!.Value.Item2, Is.EqualTo(before!.Value.Item2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DizzinessAndConfusion()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var grid = map.Grid.Owner;
        var plating = new Robust.Shared.Map.Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId);

        EntityUid steady = default, confused = default;
        await server.WaitAssertion(() =>
        {
            var mapSys = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            for (var x = 0; x < 9; x++)
            for (var y = 0; y < 12; y++)
            {
                mapSys.SetTile(grid, map.Grid.Comp, new Robust.Shared.Maths.Vector2i(x, y), plating);
            }
            var gravity = entMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(grid);
            gravity.Inherent = true;
            gravity.Enabled = true;

            var status = entMan.System<VgStatusSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.Multiple(() =>
            {
                // Dizziness: one a Life() tick, five lying down.
                status.Dizzy(human, 20);
                status.Dizzy(human, 3);
                Assert.That(status.GetDizziness(human), Is.EqualTo(20));
                LifeTick(entMan, human);
                Assert.That(status.GetDizziness(human), Is.EqualTo(19));
                entMan.System<Content.Shared.Standing.StandingStateSystem>().Down(human);
                LifeTick(entMan, human);
                Assert.That(status.GetDizziness(human), Is.EqualTo(14));

                // Confusion: one a Life() tick.
                status.Confuse(human, 3);
                LifeTick(entMan, human);
                Assert.That(status.GetConfused(human), Is.EqualTo(2));
            });

            // Two people walk north, one confused.
            steady = entMan.SpawnEntity("MobHuman", new Robust.Shared.Map.EntityCoordinates(grid, 1.5f, 1.5f));
            confused = entMan.SpawnEntity("MobHuman", new Robust.Shared.Map.EntityCoordinates(grid, 6.5f, 1.5f));
            status.Confuse(confused, 100);
            var mover = entMan.System<Content.Shared.Movement.Systems.SharedMoverController>();
            foreach (var mob in new[] { steady, confused })
            {
                mover.SetVelocityDirection((mob, entMan.GetComponent<Content.Shared.Movement.Components.InputMoverComponent>(mob)), Robust.Shared.Maths.Direction.North, 0, true);
            }
        });

        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var steadyPos = entMan.GetComponent<TransformComponent>(steady).LocalPosition;
            var confusedPos = entMan.GetComponent<TransformComponent>(confused).LocalPosition;
            Assert.Multiple(() =>
            {
                Assert.That(steadyPos.Y, Is.GreaterThan(2.5f).And.LessThan(11), "walked north, not off the floor");
                Assert.That(MathF.Abs(steadyPos.X - 1.5f), Is.LessThan(0.05f), "straight");
                // Every step goes off at 45 degrees to one side or the other, so it gets about 71% as far north.
                Assert.That(confusedPos.Y - 1.5f, Is.LessThan((steadyPos.Y - 1.5f) * 0.85f), "confused steps aren't straight");
                Assert.That(confusedPos.Y - 1.5f, Is.GreaterThan((steadyPos.Y - 1.5f) * 0.55f), "but still go forwards");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SleepAndShakingAwake()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = entMan.System<VgStatusSystem>();
            var effects = entMan.System<StatusEffectsSystem>();
            var sleeper = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var knockedOut = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var helper = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.Multiple(() =>
            {
                // vg13 sleeping: asleep, showing as SS14's forced sleep.
                status.Sleep(sleeper, 5);
                status.Paralyse(sleeper, 2);
                Assert.That(status.IsAsleep(sleeper));
                Assert.That(entMan.HasComponent<ForcedSleepingComponent>(sleeper));

                // Shaking takes 10 ticks of sleep and 3 of paralysis: both gone.
                status.Shake(sleeper);
                Assert.That(status.IsAsleep(sleeper), Is.False, "shaken awake");
                Assert.That(effects.HasStatusEffect(sleeper, "ForcedSleep"), Is.False, "no longer out cold");

                // Paralysis is kept apart from sleep: a long one outlasts a shake.
                status.Paralyse(knockedOut, 10);
                status.Sleep(knockedOut, 1);
                status.Shake(knockedOut);
                Assert.That(status.IsAsleep(knockedOut), Is.False);
                Assert.That(effects.TryGetTime(knockedOut, "ForcedSleep", out var time) && time!.Value.Item2 - server.Timing.CurTime > TimeSpan.FromSeconds(10),
                    "7 ticks of paralysis left");

                // The help intent shakes someone lying down.
                status.Sleep(sleeper, 5);
                entMan.System<Content.Shared._VG.Combat.VgCombatSystem>().SetIntent(helper, Content.Shared._VG.Combat.VgIntent.Help);
                Assert.That(entMan.System<Content.Shared._VG.Combat.VgCombatSystem>().TryUnarmed(helper, sleeper), "shook them");
                Assert.That(status.IsAsleep(sleeper), Is.False, "and woke them");
            });
        });

        await pair.CleanReturnAsync();
    }
}
