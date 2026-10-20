using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VG.Body;
using Content.Shared._VG.Damage;
using Content.Shared._VG.Input;
using Content.Shared._VG.Medical;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>
///     vg13 medical stacks, used through real client input on the aimed limb.
/// </summary>
[TestFixture]
[TestOf(typeof(VgMedicalSystem))]
public sealed class VgMedicalTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private VgLimb TargetLimb(string id)
    {
        return SEntMan.GetComponent<VgBodyComponent>(STarget!.Value).Limbs[id];
    }

    private async Task Hurt(string type, float amount, string zone, float sharpness = 0)
    {
        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier(ProtoMan.Index<DamageTypePrototype>(type), amount) { VgSharpness = sharpness };
            SEntMan.System<DamageableSystem>().TryChangeDamage(STarget!.Value, damage.WithVgZone(zone), true);
        });
        await RunTicks(1);
    }

    private async Task ClickTarget()
    {
        await PressKey(EngineKeyFunctions.Use, cursorEntity: Target);
        await RunTicks(5);
    }

    [SetUp]
    public async Task SetUpTarget()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await PressKey(VgKeyFunctions.VgZoneLeftArm);
    }

    [Test]
    public async Task GauzeBandagesTheAimedLimb()
    {
        await Hurt("Slash", 20, VgLimbs.LeftArm);
        Assert.That((TargetLimb(VgLimbs.LeftArm).Status & VgLimbStatus.Bleeding) != 0);

        var gauze = await PlaceInHands("VgBruisePack", 5);
        await ClickTarget();

        var arm = TargetLimb(VgLimbs.LeftArm);
        Assert.Multiple(() =>
        {
            Assert.That(arm.Wounds.Where(w => !w.Bandaged), Is.Empty);
            Assert.That((arm.Status & VgLimbStatus.Bleeding) == 0);
            Assert.That(SEntMan.GetComponent<StackComponent>(ToServer(gauze)).Count, Is.EqualTo(4));
        });

        // Again does nothing: they're already bandaged.
        await ClickTarget();
        Assert.That(SEntMan.GetComponent<StackComponent>(ToServer(gauze)).Count, Is.EqualTo(4));
    }

    [Test]
    public async Task AdvancedBurnKitSalvesAndHeals()
    {
        await Hurt("Heat", 30, VgLimbs.LeftArm);
        var before = TargetLimb(VgLimbs.LeftArm).BurnDamage;

        await PlaceInHands("VgAdvancedBurnKit");
        await ClickTarget();

        var arm = TargetLimb(VgLimbs.LeftArm);
        Assert.Multiple(() =>
        {
            Assert.That(arm.Wounds.All(w => w.Salved));
            // rand(10, 15)
            Assert.That(before - arm.BurnDamage, Is.InRange(10f, 15f));
        });
    }

    [Test]
    public async Task SplintTakesTime()
    {
        await PlaceInHands("VgSplint");
        await PressKey(EngineKeyFunctions.Use, cursorEntity: Target);
        await RunTicks(5);
        Assert.That((TargetLimb(VgLimbs.LeftArm).Status & VgLimbStatus.Splinted) == 0);

        await AwaitDoAfters();
        Assert.That((TargetLimb(VgLimbs.LeftArm).Status & VgLimbStatus.Splinted) != 0);
    }

    [Test]
    public async Task UpstreamItemsAreVgItems()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "Brutepack", "Gauze", "Ointment", "Brutepack1", "Gauze10Lingering" })
            {
                Assert.That(ProtoMan.Index<Robust.Shared.Prototypes.EntityPrototype>(id).TryGetComponent<VgMedicalComponent>(out _, Factory), id);
            }
        });
    }
}
