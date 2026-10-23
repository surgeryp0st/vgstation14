#nullable enable
using Content.Server._VG.Temperature;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared._VG.Temperature;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 body temperature: heat lost and gained through clothing, regulation, damage and hypothermia.</summary>
[TestFixture]
public sealed class VgBodyTemperatureTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static float Temperature(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<TemperatureComponent>(uid).CurrentTemperature;
    }

    private static void SetTemperature(IEntityManager entMan, EntityUid uid, float temperature)
    {
        entMan.System<TemperatureSystem>().ForceChangeTemperature(uid, temperature);
    }

    private static EntityUid Human(IEntityManager entMan, EntityCoordinates at, params string[] clothes)
    {
        var human = entMan.SpawnEntity("MobHuman", at);
        entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(human);
        var inventory = entMan.System<InventorySystem>();
        foreach (var item in clothes)
        {
            var slot = item.Contains("Outer") ? "outerClothing" : item.Contains("Helmet") ? "head" : "jumpsuit";
            Assert.That(inventory.SpawnItemInSlot(human, slot, item, true, true), item);
        }

        return human;
    }

    [Test]
    public async Task StagesAndDamage()
    {
        await using var pair = await PoolManager.GetServerClient();

        Assert.Multiple(() =>
        {
            Assert.That(VgBodyTemperatureSystem.Stage(310.15f, null), Is.EqualTo(VgHypothermia.None));
            Assert.That(VgBodyTemperatureSystem.Stage(Atmospherics.T0C + 33, null), Is.EqualTo(VgHypothermia.Mild));
            Assert.That(VgBodyTemperatureSystem.Stage(Atmospherics.T0C + 30, null), Is.EqualTo(VgHypothermia.Moderate));
            Assert.That(VgBodyTemperatureSystem.Stage(Atmospherics.T0C + 25, null), Is.EqualTo(VgHypothermia.Severe));
            Assert.That(VgBodyTemperatureSystem.Stage(Atmospherics.T0C + 10, null), Is.EqualTo(VgHypothermia.Profound));
            Assert.That(VgBodyTemperatureSystem.Stage(Atmospherics.T0C + 10, null, inCryo: true), Is.EqualTo(VgHypothermia.None));

            Assert.That(VgBodyTemperatureSystem.TemperatureDamage(210, null), Is.EqualTo(0.5f));
            Assert.That(VgBodyTemperatureSystem.TemperatureDamage(150, null), Is.EqualTo(1.5f));
            Assert.That(VgBodyTemperatureSystem.TemperatureDamage(100, null), Is.EqualTo(3f));
            // vg13 checks heat level 1 first, so it's level 1 however hot.
            Assert.That(VgBodyTemperatureSystem.TemperatureDamage(2000, null), Is.EqualTo(2f));

            // A jumpsuit and shoes' worth of skin.
            Assert.That(VgBodyTemperatureSystem.CoverProtection(Content.Shared._VG.Body.VgBodyParts.FullBody), Is.EqualTo(1).Within(0.001));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ColdAirAndClothes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            // Test maps are vacuum: a naked human loses heat fast, someone in a hardsuit hardly at all.
            var naked = Human(entMan, map.GridCoords);
            var suited = Human(entMan, map.GridCoords, "ClothingOuterHardsuitBasic", "ClothingHeadHelmetHardsuitBasic");
            var start = Temperature(entMan, naked);
            LifeTick(entMan, naked);
            LifeTick(entMan, suited);

            Assert.Multiple(() =>
            {
                // (skin 306.4 K - space 2.7 K) / 200, less the calories burnt.
                Assert.That(start - Temperature(entMan, naked), Is.EqualTo(1.47f).Within(0.05));
                Assert.That(start - Temperature(entMan, suited), Is.LessThan(0.2f));
                Assert.That(entMan.System<VgBodyTemperatureSystem>().ColdProtection(suited), Is.GreaterThan(0.85f));
            });

            // Freezing: burns, hypothermia, and the cold slows you down.
            SetTemperature(entMan, naked, Atmospherics.T0C + 30);
            var before = entMan.GetComponent<DamageableComponent>(naked).TotalDamage;
            LifeTick(entMan, naked);
            var comp = entMan.GetComponent<VgBodyTemperatureComponent>(naked);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Hypothermia, Is.EqualTo(VgHypothermia.Moderate));
                Assert.That(comp.Tally, Is.EqualTo(4));
                Assert.That(entMan.GetComponent<DamageableComponent>(naked).TotalDamage, Is.EqualTo(before), "no burns above 220 K");
                Assert.That(entMan.System<AlertsSystem>().IsShowingAlert(naked, "Cold"));
            });

            SetTemperature(entMan, naked, 150);
            LifeTick(entMan, naked);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Hypothermia, Is.EqualTo(VgHypothermia.Profound));
                Assert.That(entMan.System<VgStatusSystem>().IsParalysed(naked), "out cold");
                Assert.That(entMan.GetComponent<DamageableComponent>(naked).TotalDamage - before, Is.GreaterThanOrEqualTo(Content.Shared.FixedPoint.FixedPoint2.New(1.25)), "1.5, less what the wound heals");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotAirSweatAndFireSuits()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var at = new EntityCoordinates(grid, 1.5f, 0.5f);
            var normal = Human(entMan, at);

            // Too warm in ordinary air: sweat off up to 2 K a tick.
            SetTemperature(entMan, normal, 320);
            LifeTick(entMan, normal);
            Assert.That(Temperature(entMan, normal), Is.LessThan(318.5f));

            // Hot air heats you up to 10 K a tick, unless your clothes are rated for it.
            var air = entMan.System<AtmosphereSystem>().GetTileMixture(grid, null, new Vector2i(1, 0), true)!;
            air.Temperature = 1500;
            var naked = Human(entMan, at);
            var fireSuit = Human(entMan, at, "ClothingOuterSuitFire", "ClothingHeadHelmetFire");
            var start = Temperature(entMan, naked);
            LifeTick(entMan, naked);
            LifeTick(entMan, fireSuit);
            Assert.Multiple(() =>
            {
                Assert.That(Temperature(entMan, naked) - start, Is.EqualTo(10).Within(0.1));
                Assert.That(Temperature(entMan, fireSuit) - start, Is.LessThan(4), "a fire suit and helmet cover most of you");
            });

            // Heat damage is vg13's, not SS14's.
            SetTemperature(entMan, naked, 380);
            var before = entMan.GetComponent<DamageableComponent>(naked).TotalDamage;
            LifeTick(entMan, naked);
            // 2, less what the burn wound heals on the same Life() tick.
            Assert.That((entMan.GetComponent<DamageableComponent>(naked).TotalDamage - before).Float(), Is.EqualTo(2).Within(0.25));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeedFollowsHypothermia()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = Human(entMan, map.GridCoords);
            var speed = entMan.GetComponent<MovementSpeedModifierComponent>(human);
            var warm = speed.CurrentSprintSpeed;

            entMan.GetComponent<VgBodyTemperatureComponent>(human).Tally = 4;
            entMan.System<Content.Shared.Movement.Systems.MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(human);
            Assert.That(speed.CurrentSprintSpeed, Is.LessThan(warm * 0.6f), "moderate hypothermia: +4 move delay");
        });

        await pair.CleanReturnAsync();
    }
}
