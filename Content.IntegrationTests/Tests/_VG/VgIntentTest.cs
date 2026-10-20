using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Input;
using Content.Shared.Actions;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>
///     vg13 intents and zone selection, driven through real client input so prediction is exercised too.
/// </summary>
[TestFixture]
[TestOf(typeof(VgCombatSystem))]
public sealed class VgIntentTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private VgIntentComponent ServerIntent => SEntMan.GetComponent<VgIntentComponent>(SPlayer);
    private VgIntentComponent ClientIntent => CEntMan.GetComponent<VgIntentComponent>(CPlayer);

    private float TargetLimbBrute()
    {
        return SEntMan.GetComponent<VgBodyComponent>(STarget!.Value).Limbs.Values.Sum(l => l.BruteDamage);
    }

    /// <summary>
    ///     A left click on the target, through the client's input like a player's.
    /// </summary>
    private async Task Click()
    {
        await PressKey(EngineKeyFunctions.Use, cursorEntity: Target);
        // vg13 click delay on mobs, plus SS14 attack cooldowns.
        await RunSeconds(1.1f);
    }

    [Test]
    public async Task HotkeysSetIntentAndZone()
    {
        Assert.That(ServerIntent.Intent, Is.EqualTo(VgIntent.Help));

        await PressKey(VgKeyFunctions.VgIntentHarm);
        await PressKey(VgKeyFunctions.VgZoneLeftLeg);
        await RunTicks(5);

        Assert.Multiple(() =>
        {
            Assert.That(ServerIntent.Intent, Is.EqualTo(VgIntent.Harm));
            Assert.That(ClientIntent.Intent, Is.EqualTo(VgIntent.Harm));
            Assert.That(ServerIntent.Zone, Is.EqualTo(VgLimbs.LeftLeg));
            Assert.That(ClientIntent.Zone, Is.EqualTo(VgLimbs.LeftLeg));
        });

        // Insert cycles harm back round to help.
        await PressKey(VgKeyFunctions.VgIntentCycle);
        await RunTicks(5);
        Assert.That(ServerIntent.Intent, Is.EqualTo(VgIntent.Help));
    }

    [Test]
    public async Task NoCombatModeToggle()
    {
        var combat = SEntMan.GetComponent<CombatModeComponent>(SPlayer);
        Assert.Multiple(() =>
        {
            Assert.That(combat.IsInCombatMode, Is.False);
            Assert.That(SEntMan.GetComponent<ActionsComponent>(SPlayer).Actions, Does.Not.Contain(combat.CombatToggleActionEntity));
        });
    }

    [Test]
    public async Task HarmIntentPunches()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await PressKey(VgKeyFunctions.VgIntentHarm);

        // Punches do 0-5 and 0 misses, so give it a few goes.
        for (var i = 0; i < 10 && TargetLimbBrute() <= 0; i++)
        {
            await Click();
        }

        Assert.That(TargetLimbBrute(), Is.GreaterThan(0));
        Assert.That(TargetLimbBrute(), Is.LessThanOrEqualTo(50));
    }

    [Test]
    public async Task HelpIntentDoesNotPunch()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");

        for (var i = 0; i < 3; i++)
        {
            await Click();
        }

        Assert.That(TargetLimbBrute(), Is.EqualTo(0));
    }

    [Test]
    public async Task ItemsHitOnAnyIntent()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await PlaceInHands("BaseBallBat");

        // Help intent, as vg13: a weapon hits whatever you click with it. Aimed at the chest, so it rarely misses.
        for (var i = 0; i < 5 && TargetLimbBrute() <= 0; i++)
        {
            await Click();
        }

        Assert.That(TargetLimbBrute(), Is.GreaterThan(0));
    }

    [Test]
    public async Task AimedHitsLandOnTheZone()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await PlaceInHands("BaseBallBat");
        await PressKey(VgKeyFunctions.VgZoneLeftLeg);

        var body = SEntMan.GetComponent<VgBodyComponent>(STarget!.Value);
        for (var i = 0; i < 10 && body.Limbs[VgLimbs.LeftLeg].BruteDamage <= 0; i++)
        {
            await Click();
        }

        Assert.That(body.Limbs[VgLimbs.LeftLeg].BruteDamage, Is.GreaterThan(0));
    }

    [Test]
    public async Task DisarmPushesOrDisarms()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = STarget!.Value;
        await Server.WaitPost(() =>
        {
            var item = SEntMan.SpawnEntity("BaseBallBat", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            HandSys.TryPickupAnyHand(target, item);
        });
        await RunTicks(2);

        await PressKey(VgKeyFunctions.VgIntentDisarm);

        var standing = SEntMan.System<StandingStateSystem>();
        var hands = SEntMan.GetComponent<HandsComponent>(target);
        bool Done() => standing.IsDown(target) || hands.Hands.Values.All(h => h.IsEmpty);

        // 40% miss each time, otherwise a push or a disarm.
        for (var i = 0; i < 15 && !Done(); i++)
        {
            await Click();
        }

        Assert.That(Done());
    }
}
