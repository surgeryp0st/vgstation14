using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Input;
using Content.Shared.ActionBlocker;
using Content.Shared.Input;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>
///     vg13 grabs, through real client input.
/// </summary>
[TestFixture]
[TestOf(typeof(VgGrabSystem))]
public sealed class VgGrabTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private VgGrabState? State => SEntMan.TryGetComponent<VgGrabberComponent>(SPlayer, out var grabber) ? grabber.State : null;

    [SetUp]
    public async Task SetUpGrab()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await PressKey(VgKeyFunctions.VgIntentGrab);
        await PressKey(EngineKeyFunctions.Use, cursorEntity: Target);
        await RunTicks(5);
    }

    [Test]
    public async Task GrabIntentGrabsPassively()
    {
        Assert.Multiple(() =>
        {
            Assert.That(State, Is.EqualTo(VgGrabState.Passive));
            Assert.That(SEntMan.GetComponent<PullableComponent>(STarget!.Value).Puller, Is.EqualTo(SPlayer));
            // A passive grab doesn't stop them walking off.
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(STarget!.Value));
        });
    }

    [Test]
    public async Task UsingTheGrabTightensIt()
    {
        await PressKey(ContentKeyFunctions.UseItemInHand);
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(State, Is.EqualTo(VgGrabState.Aggressive));
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(STarget!.Value), Is.False);
        });

        // vg13's one second delay between upgrades.
        await RunSeconds(1.1f);
        await PressKey(ContentKeyFunctions.UseItemInHand);
        await RunTicks(5);
        Assert.That(State, Is.EqualTo(VgGrabState.Neck));
    }

    [Test]
    public async Task DroppingTheGrabLetsGo()
    {
        await PressKey(ContentKeyFunctions.UseItemInHand);
        await RunTicks(5);
        Assert.That(State, Is.EqualTo(VgGrabState.Aggressive));

        await PressKey(ContentKeyFunctions.Drop);
        await RunTicks(5);

        Assert.Multiple(() =>
        {
            Assert.That(State, Is.Null);
            Assert.That(SEntMan.HasComponent<VgGrabbedComponent>(STarget!.Value), Is.False);
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(STarget!.Value));
        });
    }
}
