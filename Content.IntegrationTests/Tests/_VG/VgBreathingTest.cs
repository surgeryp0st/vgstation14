using System.Collections.Generic;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VG.Breathing;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgBreathingSystem))]
public sealed class VgBreathingTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private float Oxygen => SEntMan.GetComponent<DamageableComponent>(SPlayer).Damage.DamageDict.GetValueOrDefault("Oxygen").Float();

    private async Task LifeTick()
    {
        await Server.WaitPost(() =>
        {
            var ev = new VgLifeTickEvent(1);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref ev);
        });
    }

    private async Task Suffocate(float amount)
    {
        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier(ProtoMan.Index<DamageTypePrototype>("Asphyxiation"), amount);
            SEntMan.System<DamageableSystem>().TryChangeDamage(SPlayer, damage, true);
        });
    }

    [Test]
    public async Task AirHeals()
    {
        await AddAtmosphere();
        await Suffocate(10);
        await LifeTick();
        // A good breath takes 5 oxygen damage off (the respirator doesn't run on vg13 bodies).
        Assert.That(Oxygen, Is.EqualTo(5).Within(0.5));
    }

    [Test]
    public async Task VacuumSuffocates()
    {
        var before = Oxygen;
        await LifeTick();
        Assert.That(Oxygen - before, Is.EqualTo(1).Within(0.01));
    }

    [Test]
    public async Task LoseBreathSkipsBreaths()
    {
        await AddAtmosphere();
        await Server.WaitPost(() => SEntMan.System<VgStatusSystem>().AddLoseBreath(SPlayer, 2));
        await LifeTick();
        Assert.Multiple(() =>
        {
            Assert.That(Oxygen, Is.EqualTo(1).Within(0.01));
            Assert.That(SEntMan.GetComponent<VgStatusComponent>(SPlayer).LoseBreath, Is.EqualTo(1));
        });
    }
}
