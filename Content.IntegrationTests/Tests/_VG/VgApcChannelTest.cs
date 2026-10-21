#nullable enable
using Content.Server._VG.Power;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._VG.Power;
using Content.Shared.Coordinates;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgApcChannelTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: VgApcDummy
  components:
  - type: Battery
    maxCharge: 10000
    startingCharge: 10000
  - type: PowerNetworkBattery
    maxChargeRate: 1000
    maxSupply: 1000
    supplyRampTolerance: 1000
  - type: BatteryCharger
    voltage: Medium
  - type: BatteryDischarger
    voltage: Apc
  - type: Apc
    voltage: Apc
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: MVPower
      output:
        !type:CableDeviceNode
        nodeGroupID: Apc
  - type: Transform
    anchored: true
  - type: UserInterface
    interfaces:
      enum.ApcUiKey.Key:
        type: ApcBoundUserInterface
  - type: AccessReader
    access: [['Engineering']]

- type: entity
  id: VgReceiverDummy
  components:
  - type: ApcPowerReceiver
  - type: ExtensionCableReceiver
  - type: Transform
    anchored: true

- type: entity
  id: VgLightReceiverDummy
  components:
  - type: ApcPowerReceiver
  - type: ExtensionCableReceiver
  - type: VgPowerChannel
    channel: Lighting
  - type: Transform
    anchored: true
";

    [Test]
    public async Task ChannelsCutTheirMachinesAndFollowTheBattery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var extensionSys = entMan.System<ExtensionCableSystem>();
        var channelSys = entMan.System<VgApcChannelSystem>();

        EntityUid apc = default;
        ApcPowerReceiverComponent equipment = default!;
        ApcPowerReceiverComponent lighting = default!;

        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            for (var i = 0; i < 5; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
            }

            apc = entMan.SpawnEntity("VgApcDummy", grid.Owner.ToCoordinates(0, 0));
            var extension = entMan.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
            var equipmentEnt = entMan.SpawnEntity("VgReceiverDummy", grid.Owner.ToCoordinates(0, 2));
            var lightingEnt = entMan.SpawnEntity("VgLightReceiverDummy", grid.Owner.ToCoordinates(0, 3));
            equipment = entMan.GetComponent<ApcPowerReceiverComponent>(equipmentEnt);
            lighting = entMan.GetComponent<ApcPowerReceiverComponent>(lightingEnt);

            extensionSys.SetProviderTransferRange(extension, 5);
            extensionSys.SetReceiverReceptionRange(equipmentEnt, 5);
            extensionSys.SetReceiverReceptionRange(lightingEnt, 5);

            var battery = entMan.GetComponent<BatteryComponent>(apc);
            batterySys.SetMaxCharge(apc, 10000, battery);
            batterySys.SetCharge(apc, battery.MaxCharge, battery);

            equipment.Load = 1;
            lighting.Load = 1;
        });

        await server.WaitRunTicks(70);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<VgApcChannelsComponent>(apc), "the APC got its channels");
                Assert.That(equipment.Powered, "equipment is on");
                Assert.That(lighting.Powered, "lighting is on");
            });

            // Switching the lighting channel off cuts the lights and nothing else.
            channelSys.SetChannel((apc, entMan.GetComponent<VgApcChannelsComponent>(apc)), VgPowerChannel.Lighting, VgApcChannelState.Off);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(equipment.Powered, "equipment is still on");
                Assert.That(lighting.Powered, Is.False, "lighting is off");
            });

            channelSys.SetChannel((apc, entMan.GetComponent<VgApcChannelsComponent>(apc)), VgPowerChannel.Lighting, VgApcChannelState.AutoOn);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(lighting.Powered, "lighting is back");
            Assert.That(lighting.Load, Is.EqualTo(1), "and draws what it did");

            // A nearly empty battery that can't charge cuts equipment first (below 30%), then lighting too (below 15%).
            var channels = entMan.GetComponent<VgApcChannelsComponent>(apc);
            channels.LongTermPower = -10;
            var battery = entMan.GetComponent<BatteryComponent>(apc);
            batterySys.SetCharge(apc, battery.MaxCharge * 0.2f, battery);
        });

        await server.WaitRunTicks(100);

        await server.WaitAssertion(() =>
        {
            var channels = entMan.GetComponent<VgApcChannelsComponent>(apc);
            Assert.That(VgApcChannelSystem.IsOn(channels.Equipment), Is.False, "equipment cut itself");
        });

        await pair.CleanReturnAsync();
    }
}
