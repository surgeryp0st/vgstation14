#nullable enable
using System.Linq;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.NodeGroups;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Loads a map made by Tools/_VG/dmm2map and checks that its cables and pipes joined up.</summary>
[TestFixture]
public sealed class VgConvertedMapTest
{
    [Test]
    public async Task ConvertedLineMapIsWired()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            Assert.That(mapLoader.TryLoad(mapId, "Maps/_VG/line.yml", out _));
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var apcs = 0;
            var apcInputWired = 0;
            var apcOutputWired = 0;
            foreach (var (_, nodes) in entMan.EntityQuery<ApcComponent, NodeContainerComponent>())
            {
                apcs++;
                if (nodes.Nodes["input"].NodeGroup is BaseNodeGroup { NodeCount: > 1 })
                    apcInputWired++;
                if (nodes.Nodes["output"].NodeGroup is BaseNodeGroup { NodeCount: > 1 })
                    apcOutputWired++;
            }

            var receivers = 0;
            var receiversConnected = 0;
            foreach (var recv in entMan.EntityQuery<ExtensionCableReceiverComponent>())
            {
                receivers++;
                if (recv.Provider != null)
                    receiversConnected++;
            }

            var vents = 0;
            var ventsWired = 0;
            foreach (var (_, nodes) in entMan.EntityQuery<GasVentPumpComponent, NodeContainerComponent>())
            {
                vents++;
                if (nodes.Nodes.Values.OfType<PipeNode>().Any(n => n.NodeGroup is BaseNodeGroup { NodeCount: > 1 }))
                    ventsWired++;
            }

            TestContext.Out.WriteLine($"APCs {apcs}: input wired {apcInputWired}, output wired {apcOutputWired}");
            TestContext.Out.WriteLine($"power receivers {receivers}: on an APC network {receiversConnected}");
            TestContext.Out.WriteLine($"vents {vents}: on a pipe network {ventsWired}");
            Assert.That(apcs, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }
}
