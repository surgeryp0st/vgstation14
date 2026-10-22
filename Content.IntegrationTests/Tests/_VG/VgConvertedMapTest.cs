#nullable enable
using System.Linq;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server._VG.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Disposal.Tube;
using Content.Server.Disposal.Tube.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.SMES;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Loads a map made by Tools/_VG/dmm2map and checks that its cables and pipes joined up.</summary>
[TestFixture]
public sealed class VgConvertedMapTest
{
    private static bool Joined(Node node) => node.NodeGroup is BaseNodeGroup { NodeCount: > 1 };

    [Test]
    [TestCase("line")]
    [TestCase("metaclub")]
    [TestCase("tgstation")] // vg13's Box Station
    [TestCase("bagelstation")]
    [TestCase("defficiency")]
    [TestCase("dorfstation")]
    [TestCase("horizon")]
    [TestCase("junglestation")]
    [TestCase("lampreystation")]
    [TestCase("lowfatbagel")]
    [TestCase("nervestation")]
    [TestCase("odyssey")]
    [TestCase("packedstation")]
    [TestCase("roidstation")]
    [TestCase("snaxi")]
    [TestCase("synergy")]
    [TestCase("tgstation-sec")]
    [TestCase("tgstation-snow")]
    [TestCase("theseus")]
    [TestCase("waystation")]
    [TestCase("wheelstation")]
    [TestCase("xoq")]
    public async Task ConvertedMapIsWired(string map)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        if (!server.ResolveDependency<IResourceManager>().ContentFileExists(new ResPath($"/Maps/_VG/{map}.yml")))
            Assert.Ignore($"{map}.yml has not been converted");
        var entMan = server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            Assert.That(mapLoader.TryLoad(mapId, $"Maps/_VG/{map}.yml", out _));
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

            // Maps with outposts and asteroids have separate floor islands whose machines nothing powers, so the
            // thresholds are not 100%.
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

            // Every end of a disposal pipe, apart from trunks' bin side, should have a pipe waiting that takes it.
            var disposal = entMan.System<DisposalTubeSystem>();
            var tubes = 0;
            var tubesConnected = 0;
            foreach (var (tube, _) in entMan.EntityQuery<DisposalTubeComponent, MetaDataComponent>())
            {
                tubes++;
                var ev = new GetDisposalsConnectableDirectionsEvent();
                entMan.EventBus.RaiseLocalEvent(tube.Owner, ref ev);
                if (ev.Connectable.All(d => disposal.NextTubeFor(tube.Owner, d, tube) != null))
                    tubesConnected++;
            }

            var tanks = 0;
            var tanksFull = 0;
            foreach (var (tank, nodes) in entMan.EntityQuery<VgPipeTankComponent, NodeContainerComponent>())
            {
                if (tank.Gases.Count == 0)
                    continue;
                tanks++;
                if (nodes.Nodes.Values.OfType<PipeNode>().Any(n => n.Air.Pressure > 1000))
                    tanksFull++;
            }

            TestContext.Out.WriteLine($"{map}: disposal pipes {tubesConnected}/{tubes}, filled tanks {tanksFull}/{tanks}");
            TestContext.Out.WriteLine($"{map}: SMES {smesWired}/{smes}, substations {subsBridging}/{subs}, APCs {apcsWired}/{apcs}, " +
                                      $"machines on an APC network {receiversPowered}/{receivers}, vents {ventsWired}/{vents}, " +
                                      $"pumps/valves/filters {devicesWired}/{devices}");
            Assert.Multiple(() =>
            {
                Assert.That(smesWired, Is.GreaterThanOrEqualTo(smes * 0.75), "SMES terminal and output");
                Assert.That(subsBridging, Is.GreaterThanOrEqualTo(subs * 0.75), "substations bridge HV and MV");
                Assert.That(apcsWired, Is.GreaterThanOrEqualTo(apcs * 0.75), "APC input and output");
                Assert.That(receiversPowered, Is.GreaterThanOrEqualTo(receivers * 0.75), "machines reach an APC");
                Assert.That(ventsWired, Is.GreaterThanOrEqualTo(vents * 0.75), "vents are on a pipe network");
                Assert.That(tubesConnected, Is.GreaterThanOrEqualTo(tubes * 0.75), "disposal pipes join up");
                Assert.That(tanksFull, Is.GreaterThanOrEqualTo(tanks * 0.75), "pressure tanks start full");
                Assert.That(devicesWired, Is.GreaterThanOrEqualTo(devices * 0.75), "pumps and valves sit in a pipe line");
            });
        });

        await pair.CleanReturnAsync();
    }
}
