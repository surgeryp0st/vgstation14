using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgAtmosTest
{
    [Test]
    public async Task Vg13GasesAndReactions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();

            // vg13's gases exist with their numbers.
            var heatCapacity = atmos.GetHeatCapacity(new GasMixture(1000) { Temperature = 300 }, true);
            Assert.That(heatCapacity, Is.EqualTo(0).Within(0.01), "an empty mixture has no heat capacity");

            // N2O decomposes when it's hot enough, giving off heat and nitrogen and oxygen.
            var hot = new GasMixture(Atmospherics.CellVolume) { Temperature = 900 };
            hot.AdjustMoles(Gas.NitrousOxide, 10);
            hot.AdjustMoles(Gas.Nitrogen, 20);
            atmos.React(hot, null);
            Assert.Multiple(() =>
            {
                Assert.That(hot.GetMoles(Gas.NitrousOxide), Is.LessThan(10));
                Assert.That(hot.GetMoles(Gas.Oxygen), Is.GreaterThan(0));
            });

            // Cold N2O is left alone.
            var cold = new GasMixture(Atmospherics.CellVolume) { Temperature = 300 };
            cold.AdjustMoles(Gas.NitrousOxide, 10);
            atmos.React(cold, null);
            Assert.That(cold.GetMoles(Gas.NitrousOxide), Is.EqualTo(10).Within(0.001));

            // Cryotheum slowly uses up oxygen and chills the mixture.
            var cryo = new GasMixture(Atmospherics.CellVolume) { Temperature = 300 };
            cryo.AdjustMoles(Gas.Oxygen, 20);
            cryo.AdjustMoles(Gas.Cryotheum, 2);
            atmos.React(cryo, null);
            Assert.Multiple(() =>
            {
                Assert.That(cryo.GetMoles(Gas.Oxygen), Is.LessThan(20));
                Assert.That(cryo.Temperature, Is.LessThan(300));
            });

            // With plasma around, cryotheum burns itself up and drops the temperature a lot.
            var plasma = new GasMixture(Atmospherics.CellVolume) { Temperature = 300 };
            plasma.AdjustMoles(Gas.Plasma, 20);
            plasma.AdjustMoles(Gas.Cryotheum, 2);
            atmos.React(plasma, null);
            Assert.Multiple(() =>
            {
                Assert.That(plasma.GetMoles(Gas.Cryotheum), Is.LessThan(2));
                Assert.That(plasma.Temperature, Is.LessThan(250));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadonIrradiatesMobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(human);

            var air = atmos.GetContainingMixture(human);
            Assert.That(air, Is.Not.Null);
            air!.AdjustMoles(Gas.Radon, 2);
            var expected = air.GetMoles(Gas.Radon) / air.Volume * Atmospherics.CellVolume * 1.5f;

            var ev = new Content.Shared._VG.Life.VgLifeTickEvent(1);
            entMan.EventBus.RaiseLocalEvent(human, ref ev);

            var dose = entMan.GetComponent<Content.Shared._VG.Radiation.VgRadiationComponent>(human).Radiation;
            // The tick's own dose ladder burns a little off afterwards.
            Assert.That(dose, Is.GreaterThan(expected - 5).And.LessThanOrEqualTo(expected));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GasOverlaysFollowVg13()
    {
        // vg13 shows plasma and cryotheum above 0.7 mol in a tile and N2O above 1, fully and at once; the rest of its
        // gases never show.
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var overlay = server.System<GasTileOverlaySystem>();
            byte Opacity(Gas gas, float moles)
            {
                var air = new GasMixture(Atmospherics.CellVolume);
                air.AdjustMoles(gas, moles);
                var index = System.Array.IndexOf(overlay.VisibleGasId, (int) gas);
                return index < 0 ? (byte) 0 : overlay.GetOverlayData(air).Opacity[index];
            }

            Assert.Multiple(() =>
            {
                foreach (var (gas, limit) in new[] { (Gas.Plasma, 0.7f), (Gas.Cryotheum, 0.7f), (Gas.NitrousOxide, 1f) })
                {
                    Assert.That(overlay.VisibleGasId, Does.Contain((int) gas), $"{gas} has an overlay");
                    Assert.That(Opacity(gas, limit * 0.9f), Is.Zero, $"{gas} below the limit");
                    Assert.That(Opacity(gas, limit * 1.05f), Is.EqualTo(byte.MaxValue), $"{gas} just past the limit");
                }

                foreach (var gas in new[] { Gas.VolatileFuel, Gas.OxygenAgentB, Gas.Radon })
                {
                    Assert.That(overlay.VisibleGasId, Does.Not.Contain((int) gas), $"{gas} is invisible");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
