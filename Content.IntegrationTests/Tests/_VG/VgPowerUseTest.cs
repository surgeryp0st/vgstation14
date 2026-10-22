#nullable enable
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgPowerUseTest
{
    [TestCase("ComputerPowerMonitoring", 300)]
    [TestCase("ComputerResearchAndDevelopment", 300)] // SS14 had 1000; it inherits vg13's computer figure
    [TestCase("ComputerCrewMonitoring", 250)]
    [TestCase("Autolathe", 50)]
    [TestCase("Protolathe", 20)]
    [TestCase("ChemMaster", 20)]
    [TestCase("KitchenMicrowave", 5)]
    [TestCase("MedicalScanner", 125)]
    public async Task MachinesDrawVgIdlePower(string proto, int watts)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var machine = server.EntMan.SpawnEntity(proto, map.GridCoords);
            Assert.That(server.EntMan.GetComponent<ApcPowerReceiverComponent>(machine).Load, Is.EqualTo(watts));
        });

        await pair.CleanReturnAsync();
    }
}
