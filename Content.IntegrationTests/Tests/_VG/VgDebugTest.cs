#nullable enable
using Content.Server.Atmos.Components;
using Content.Server._VG.Administration;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgDebugTest
{
    [Test]
    public async Task DebugActionsDoWhatTheySay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var debug = entMan.System<VgDebugSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            bool Do(VgDebugAction action, string zone = VgLimbs.Chest) => debug.Perform(null, human, action, zone, out _);

            Assert.Multiple(() =>
            {
                Assert.That(Do(VgDebugAction.BreakLimb, VgLimbs.LeftArm), Is.True);
                Assert.That(body.Limbs[VgLimbs.LeftArm].IsBroken, Is.True, "break-limb");

                Assert.That(Do(VgDebugAction.InternalBleeding, VgLimbs.RightArm), Is.True);
                Assert.That(body.Limbs[VgLimbs.RightArm].InternallyBleeding, Is.True, "internal-bleeding");

                Assert.That(Do(VgDebugAction.BreakOrgans, VgLimbs.Chest), Is.True);
                foreach (var organ in body.Organs.Values)
                {
                    if (organ.Parent == VgLimbs.Chest)
                        Assert.That(organ.IsBroken, Is.True, $"break-organs: {organ.Id}");
                }

                Assert.That(Do(VgDebugAction.SeverLimb, VgLimbs.LeftLeg), Is.True);
                Assert.That(body.Limbs[VgLimbs.LeftLeg].IsExisting, Is.False, "sever-limb");
                // Gone already, and the torso never comes off.
                Assert.That(Do(VgDebugAction.SeverLimb, VgLimbs.LeftLeg), Is.False);
                Assert.That(Do(VgDebugAction.SeverLimb, VgLimbs.Chest), Is.False);

                Assert.That(Do(VgDebugAction.Halloss), Is.True);
                Assert.That(entMan.GetComponent<VgStatusComponent>(human).Halloss, Is.GreaterThanOrEqualTo(VgDebugSystem.HallossAmount));

                var ticks = entMan.GetComponent<VgLifeComponent>(human).LifeTick;
                Assert.That(Do(VgDebugAction.LifeTicks), Is.True);
                Assert.That(entMan.GetComponent<VgLifeComponent>(human).LifeTick, Is.EqualTo(ticks + VgDebugSystem.LifeTickCount));

                Assert.That(Do(VgDebugAction.Ignite), Is.True);
                Assert.That(entMan.GetComponent<FlammableComponent>(human).OnFire, Is.True, "ignite");
                Assert.That(Do(VgDebugAction.Extinguish), Is.True);
                Assert.That(entMan.GetComponent<FlammableComponent>(human).OnFire, Is.False, "extinguish");

                Assert.That(debug.Perform(null, human, VgDebugAction.Readout, VgLimbs.Chest, out var readout), Is.True);
                Assert.That(readout, Does.Contain("broken").And.Contain("internal bleeding").And.Contain("severed"));
            });

            // A crowbar has no body: only the readout applies.
            var crowbar = entMan.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(debug.Applies(crowbar, VgDebugAction.BreakLimb), Is.False);
            Assert.That(debug.Perform(null, crowbar, VgDebugAction.BreakLimb, VgLimbs.Chest, out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DebugCommand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid human = default;
        await server.WaitPost(() => human = entMan.SpawnEntity("MobHuman", map.GridCoords));
        await server.WaitPost(() =>
            server.ConsoleHost.ExecuteCommand($"vgdebug break-limb {entMan.GetNetEntity(human)} {VgLimbs.RightLeg}"));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<VgBodyComponent>(human).Limbs[VgLimbs.RightLeg].IsBroken, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
