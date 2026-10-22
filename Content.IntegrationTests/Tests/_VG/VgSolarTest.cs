#nullable enable
using Content.Server.Power.Components;
using Content.Server.Solar.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgSolarTest
{
    /// <summary>vg13's solar panel makes SOLARGENRATE (1500) times cos squared of the angle to the sun.</summary>
    [TestCase(0, 1500)]
    [TestCase(60, 375)]
    [TestCase(80, 45)]
    [TestCase(100, 0)]
    public async Task PanelOutputFollowsCosSquared(int degrees, int expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        // The solar system is internal to Content.Server, so it is driven by reflection.
        var type = System.Type.GetType("Content.Server.Solar.EntitySystems.PowerSolarSystem, Content.Server")!;
        var system = entMan.EntitySysManager.GetEntitySystem(type);
        EntityUid panel = default;

        await server.WaitPost(() =>
        {
            type.GetField("SunAngularVelocity")!.SetValue(system, Angle.Zero);
            type.GetField("TowardsSun")!.SetValue(system, Angle.Zero);
            type.GetField("TargetPanelVelocity")!.SetValue(system, Angle.Zero);
            type.GetField("TargetPanelRotation")!.SetValue(system, Angle.FromDegrees(degrees));
            panel = entMan.SpawnEntity("SolarPanel", map.GridCoords);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SolarPanelComponent>(panel).MaxSupply, Is.EqualTo(1500));
            Assert.That(entMan.GetComponent<PowerSupplierComponent>(panel).MaxSupply, Is.EqualTo(expected).Within(1));
        });

        await pair.CleanReturnAsync();
    }
}
