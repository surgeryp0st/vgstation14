using Content.Shared._VG.Body;
using Content.Shared._VG.Movement;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgMovementSystem))]
public sealed class VgMovementTest
{
    [Test]
    public void DelayToSpeed()
    {
        Assert.Multiple(() =>
        {
            // Run (1 ds) lands on the 4th 0.33 ds tick, walk (4 ds) on the 13th.
            Assert.That(VgMovementSystem.SpeedOf(1), Is.EqualTo(10 / 1.32f).Within(0.01));
            Assert.That(VgMovementSystem.SpeedOf(4), Is.EqualTo(10 / 4.29f).Within(0.01));
        });
    }

    [Test]
    public async Task SlowdownsFollowVg13()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = entMan.System<InventorySystem>();
            var speed = entMan.System<MovementSpeedModifierSystem>();
            var move = entMan.GetComponent<MovementSpeedModifierComponent>(human);

            var shoes = entMan.SpawnEntity("ClothingShoesColorBlack", map.GridCoords);
            Assert.That(inventory.TryEquip(human, shoes, "shoes", silent: true, force: true));
            speed.RefreshMovementSpeedModifiers(human);
            var shod = move.CurrentSprintSpeed;
            Assert.That(shod, Is.EqualTo(VgMovementSystem.SpeedOf(1)).Within(0.01));

            // Bare feet: 1.4x the delay.
            Assert.That(inventory.TryUnequip(human, "shoes", silent: true, force: true));
            speed.RefreshMovementSpeedModifiers(human);
            Assert.That(move.CurrentSprintSpeed, Is.EqualTo(VgMovementSystem.SpeedOf(1.4f)).Within(0.01));

            // A space suit doubles it.
            Assert.That(inventory.TryEquip(human, shoes, "shoes", silent: true, force: true));
            var suit = entMan.SpawnEntity("ClothingOuterHardsuitEVA", map.GridCoords);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true));
            speed.RefreshMovementSpeedModifiers(human);
            Assert.That(move.CurrentSprintSpeed, Is.EqualTo(VgMovementSystem.SpeedOf(2)).Within(0.01));
            Assert.That(inventory.TryUnequip(human, "outerClothing", silent: true, force: true));

            // A broken leg: +1.5 to the tally.
            var body = entMan.GetComponent<VgBodyComponent>(human);
            body.Limbs[VgLimbs.LeftLeg].Status |= VgLimbStatus.Broken;
            speed.RefreshMovementSpeedModifiers(human);
            Assert.That(move.CurrentSprintSpeed, Is.EqualTo(VgMovementSystem.SpeedOf(2.5f)).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DrowsyCantRun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = entMan.System<InventorySystem>();
            var speed = entMan.System<MovementSpeedModifierSystem>();
            var move = entMan.GetComponent<MovementSpeedModifierComponent>(human);
            Assert.That(inventory.TryEquip(human, entMan.SpawnEntity("ClothingShoesColorBlack", map.GridCoords), "shoes", silent: true, force: true));

            entMan.System<Content.Shared._VG.Status.VgStatusSystem>().AdjustDrowsiness(human, 10);
            speed.RefreshMovementSpeedModifiers(human);

            Assert.Multiple(() =>
            {
                // vg13: running while drowsy adds 6 to the delay; walking is untouched.
                Assert.That(move.CurrentSprintSpeed, Is.EqualTo(VgMovementSystem.SpeedOf(1 + 6)).Within(0.01));
                Assert.That(move.CurrentWalkSpeed, Is.EqualTo(VgMovementSystem.SpeedOf(4)).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }
}
