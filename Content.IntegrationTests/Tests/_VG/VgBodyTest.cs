using System.Linq;
using Content.Server._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared._VG.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgBodySystem))]
public sealed class VgBodyTest
{
    private static DamageSpecifier Blunt(IPrototypeManager protoMan, float amount)
    {
        return new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), amount);
    }

    private static float LimbTotal(VgBodyComponent body, Func<VgLimb, float> damage)
    {
        return body.Limbs.Values.Where(l => l.IsOrganic && l.IsExisting).Sum(damage);
    }

    [Test]
    public async Task TargetedDamageHitsLimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var damage = entMan.GetComponent<DamageableComponent>(human);

            Assert.That(body.Limbs, Has.Count.EqualTo(11));

            damageable.TryChangeDamage(human, Blunt(protoMan, 10).WithVgZone(VgLimbs.LeftArm), true);

            var arm = body.Limbs[VgLimbs.LeftArm];
            Assert.Multiple(() =>
            {
                Assert.That(arm.BruteDamage, Is.EqualTo(10).Within(0.01));
                Assert.That(arm.Wounds, Is.Not.Empty);
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(10).Within(0.01));
                Assert.That(damage.Damage.DamageDict.ContainsKey("Blunt"), Is.False);
            });

            // Healing comes off the limbs, and the totals follow.
            damageable.TryChangeDamage(human, Blunt(protoMan, -10), true);
            Assert.Multiple(() =>
            {
                Assert.That(arm.BruteDamage, Is.EqualTo(0).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(0).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UntargetedDamageMatchesLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var damage = entMan.GetComponent<DamageableComponent>(human);

            // A heavy hit can reach an organ inside the limb, which takes half of it; keep that out of the sums.
            body.Organs.Clear();

            var burn = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Heat"), 25);
            damageable.TryChangeDamage(human, burn + Blunt(protoMan, 15), true);

            Assert.Multiple(() =>
            {
                Assert.That(LimbTotal(body, l => l.BruteDamage), Is.EqualTo(15).Within(0.01));
                Assert.That(LimbTotal(body, l => l.BurnDamage), Is.EqualTo(25).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Brute"].Float(), Is.EqualTo(15).Within(0.01));
                Assert.That(damage.Damage.DamageDict["Burn"].Float(), Is.EqualTo(25).Within(0.01));
            });

            // Admin heal / rejuvenate clears everything.
            entMan.EventBus.RaiseLocalEvent(human, new RejuvenateEvent());
            Assert.Multiple(() =>
            {
                Assert.That(body.Limbs.Values.All(l => l.Wounds.Count == 0 && l.Health == 0));
                Assert.That(damage.TotalDamage.Float(), Is.EqualTo(0).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LimbFracturesOnLifeTick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);

            // An arm breaks above 30 brute.
            damageable.TryChangeDamage(human, Blunt(protoMan, 35).WithVgZone(VgLimbs.LeftArm), true);
            Assert.That(body.Limbs[VgLimbs.LeftArm].IsBroken, Is.False);

            var tick = new VgLifeTickEvent(1);
            entMan.EventBus.RaiseLocalEvent(human, ref tick);
            Assert.That(body.Limbs[VgLimbs.LeftArm].IsBroken, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharpEnoughHitSeversLimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();
        var mobState = entMan.System<MobStateSystem>();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.GetComponent<VgBodyComponent>(human);
            var hands = entMan.GetComponent<HandsComponent>(human);
            var handsBefore = hands.Count;

            // vg13 treats sharpness 100 as always able to sever.
            var slash = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Slash"), 10) { VgSharpness = 100 };
            damageable.TryChangeDamage(human, slash.WithVgZone(VgLimbs.RightArm), true);

            var arm = body.Limbs[VgLimbs.RightArm];
            var hand = body.Limbs[VgLimbs.RightHand];
            Assert.Multiple(() =>
            {
                Assert.That(arm.IsExisting, Is.False);
                Assert.That(hand.IsExisting, Is.False);
                Assert.That(hand.Amputated, Is.True);
                Assert.That(mobState.IsAlive(human), Is.True);
            });
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var human = entMan.EntityQuery<VgBodyComponent>().Select(b => b.Owner).First();
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<HandsComponent>(human).Count, Is.EqualTo(1));
                Assert.That(entMan.EntityQuery<MetaDataComponent>().Any(m => m.EntityPrototype?.ID == "VgLimbRightArm"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientGetsLimbState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEntMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = sEntMan.System<DamageableSystem>();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            // Air, so barotrauma doesn't keep adding brute while the client catches up.
            var air = new Content.Shared.Atmos.GasMixture(Content.Shared.Atmos.Atmospherics.CellVolume) { Temperature = Content.Shared.Atmos.Atmospherics.T20C };
            air.AdjustMoles(Content.Shared.Atmos.Gas.Oxygen, 21.824779f);
            air.AdjustMoles(Content.Shared.Atmos.Gas.Nitrogen, 82.10312f);
            sEntMan.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            human = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            damageable.TryChangeDamage(human, Blunt(protoMan, 12).WithVgZone(VgLimbs.LeftLeg), true);
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var cEntMan = client.EntMan;
            var clientHuman = cEntMan.GetEntity(sEntMan.GetNetEntity(human));
            var serverBody = sEntMan.GetComponent<VgBodyComponent>(human);
            var serverLeg = serverBody.Limbs[VgLimbs.LeftLeg];
            var clientBody = cEntMan.GetComponent<VgBodyComponent>(clientHuman);
            var clientLeg = clientBody.Limbs[VgLimbs.LeftLeg];

            Assert.Multiple(() =>
            {
                // The client got the server's limbs, and didn't route the damage again while applying the state.
                Assert.That(clientLeg.BruteDamage, Is.EqualTo(serverLeg.BruteDamage).Within(0.01));
                Assert.That(LimbTotal(clientBody, l => l.BruteDamage),
                    Is.EqualTo(LimbTotal(serverBody, l => l.BruteDamage)).Within(0.01));
                Assert.That(clientLeg.Wounds, Has.Count.EqualTo(serverLeg.Wounds.Count));
                // Its own copy, so prediction can't write into the stored server state.
                Assert.That(clientLeg, Is.Not.SameAs(serverLeg));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExamineShowsWounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var damageable = entMan.System<DamageableSystem>();
        var examine = entMan.System<ExamineSystemShared>();
        var inventory = entMan.System<InventorySystem>();

        EntityUid human = default;
        EntityUid examiner = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            examiner = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        // Identity (and with it the pronouns examine uses) updates a tick later.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            // A 20 damage slash always makes a bleeding "deep ripped cut".
            var slash = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Slash"), 20);
            damageable.TryChangeDamage(human, slash.WithVgZone(VgLimbs.LeftArm), true);

            damageable.TryChangeDamage(human, Blunt(protoMan, 4).WithVgZone(VgLimbs.Head), true);
            var text = examine.GetExamineText(human, examiner).ToString();
            Assert.That(text, Does.Contain("bleeding deep ripped cut"));
            Assert.That(text, Does.Contain("left arm"));

            // A jumpsuit hides the arm, but the blood still shows.
            var suit = entMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(inventory.TryEquip(human, suit, "jumpsuit", silent: true, force: true));
            text = examine.GetExamineText(human, examiner).ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Not.Contain("deep ripped cut"));
                Assert.That(text, Does.Contain("blood soaking through"));
            });

            // Missing limbs show through clothing.
            var cut = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Slash"), 10) { VgSharpness = 100 };
            damageable.TryChangeDamage(human, cut.WithVgZone(VgLimbs.RightLeg), true);
            text = examine.GetExamineText(human, examiner).ToString();
            Assert.That(text, Does.Contain("missing"));
            Assert.That(text, Does.Contain("right leg"));
        });

        await pair.CleanReturnAsync();
    }
}
