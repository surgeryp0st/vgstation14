#nullable enable
using System.Linq;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.SMES;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Loads a map made by Tools/_VG/dmm2map and checks that its cables and pipes joined up.</summary>
[TestFixture]
public sealed class VgConvertedMapTest
{
    private static bool Joined(Node node) => node.NodeGroup is BaseNodeGroup { NodeCount: > 1 };

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
            int Count<T>(System.Func<NodeContainerComponent, bool> wired, out int total) where T : IComponent
            {
                total = 0;
                var ok = 0;
                foreach (var (_, nodes) in entMan.EntityQuery<T, NodeContainerComponent>())
                {
                    total++;
                    if (wired(nodes))
                        ok++;
                }

                return ok;
            }

            // Pieces of the map that vg13 joins by other means (separate floor islands) are left out of the totals, so
            // the thresholds are not 100%.
            var smesWired = Count<SmesComponent>(n => Joined(n.Nodes["input"]) && Joined(n.Nodes["output"]), out var smes);
            var apcsWired = Count<ApcComponent>(n => Joined(n.Nodes["input"]) && Joined(n.Nodes["output"]), out var apcs);
            var ventsWired = Count<GasVentPumpComponent>(n => n.Nodes.Values.OfType<PipeNode>().Any(Joined), out var vents);

            var receivers = 0;
            var receiversPowered = 0;
            foreach (var recv in entMan.EntityQuery<ExtensionCableReceiverComponent>())
            {
                receivers++;
                if (recv.Provider != null)
                    receiversPowered++;
            }

            var subs = 0;
            var subsBridging = 0;
            var devices = 0;
            var devicesWired = 0;
            foreach (var (meta, nodes) in entMan.EntityQuery<MetaDataComponent, NodeContainerComponent>())
            {
                switch (meta.EntityPrototype?.ID)
                {
                    case "SubstationBasic":
                        subs++;
                        if (nodes.Nodes.Values.All(Joined))
                            subsBridging++;
                        break;
                    case "GasPressurePump" or "GasVolumePump" or "GasValve" or "GasFilter":
                        devices++;
                        if (nodes.Nodes.Values.OfType<PipeNode>().All(Joined))
                            devicesWired++;
                        break;
                }
            }

            TestContext.Out.WriteLine($"SMES {smesWired}/{smes}, substations {subsBridging}/{subs}, APCs {apcsWired}/{apcs}, " +
                                      $"machines on an APC network {receiversPowered}/{receivers}, vents {ventsWired}/{vents}, " +
                                      $"pumps/valves/filters {devicesWired}/{devices}");
            Assert.Multiple(() =>
            {
                Assert.That(smesWired, Is.GreaterThanOrEqualTo(smes * 0.9), "SMES terminal and output");
                Assert.That(subsBridging, Is.GreaterThanOrEqualTo(subs * 0.8), "substations bridge HV and MV");
                Assert.That(apcsWired, Is.GreaterThanOrEqualTo(apcs * 0.9), "APC input and output");
                Assert.That(receiversPowered, Is.GreaterThanOrEqualTo(receivers * 0.9), "machines reach an APC");
                Assert.That(ventsWired, Is.GreaterThanOrEqualTo(vents * 0.9), "vents are on a pipe network");
                Assert.That(devicesWired, Is.GreaterThanOrEqualTo(devices * 0.7), "pumps and valves sit in a pipe line");
            });
        });

        await pair.CleanReturnAsync();
    }
}
