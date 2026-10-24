#nullable enable
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._VG.Movement;
using Content.Shared.Input;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 pulling: what a vg mob pulls rides a tile behind, at any speed.</summary>
public sealed class VgPullTest : MovementTest
{
    [Test]
    public async Task PulledThingStaysATileBehind()
    {
        await SpawnTarget("MobHuman");
        Assert.That(SEntMan.HasComponent<VgMovementComponent>(SPlayer));

        await PressKey(ContentKeyFunctions.TryPullObject);
        await RunTicks(5);
        var puller = Comp<PullerComponent>(Player);
        Assert.That(puller.Pulling, Is.EqualTo(STarget));
        Assert.That(SEntMan.TryGetComponent<JointComponent>(STarget, out var joints) && joints.JointCount > 0, Is.False, "no SS14 pull joint");

        // Run west at vg13 speed: the pulled human keeps up, a tile behind.
        await Move(DirectionFlag.West, 0.5f);
        Assert.That(Delta(), Is.InRange(0.8f, 1.3f));

        // And back the other way, past where it is.
        await Move(DirectionFlag.East, 1);
        Assert.That(Delta(), Is.InRange(-1.3f, -0.8f));
        Assert.That(puller.Pulling, Is.EqualTo(STarget));
    }
}
