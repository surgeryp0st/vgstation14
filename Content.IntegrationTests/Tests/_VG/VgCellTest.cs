#nullable enable
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgCellTest
{
    /// <summary>vg13's cell sizes, in its units; a handheld cell unit is 0.72 J.</summary>
    [TestCase("PowerCellPotato", 300)]
    [TestCase("PowerCellSmall", 500)]
    [TestCase("PowerCellMedium", 1000)]
    [TestCase("PowerCellHigh", 10000)]
    [TestCase("VgPowerCellSuper", 20000)]
    [TestCase("PowerCellHyper", 30000)]
    [TestCase("VgPowerCellUltra", 50000)]
    public async Task CellsHoldVgCapacity(string proto, int units)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cell = server.EntMan.SpawnEntity(proto, map.GridCoords);
            var battery = server.EntMan.GetComponent<BatteryComponent>(cell);
            Assert.That(battery.MaxCharge, Is.EqualTo(units * 0.72f).Within(0.5f));
            Assert.That(battery.CurrentCharge, Is.EqualTo(battery.MaxCharge).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }
}
