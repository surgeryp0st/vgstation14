#nullable enable
using System.Linq;
using Content.Shared._VG.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>SS14 reagents vg13's replace are swapped in every solution loaded from a prototype.</summary>
[TestFixture]
public sealed class VgReagentTranslationTest
{
    [Test]
    public async Task ReplacementsExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            foreach (var (from, to) in VgReagentTranslation.Replacements)
            {
                Assert.That(proto.HasIndex<ReagentPrototype>(from), from);
                Assert.That(proto.HasIndex<ReagentPrototype>(to), to);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MedipenHoldsVgMedicine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var pen = entMan.SpawnEntity("EmergencyMedipen", map.GridCoords);
            Assert.That(entMan.System<SharedSolutionContainerSystem>().TryGetSolution(pen, "pen", out _, out var solution));
            var reagents = solution!.Contents.Select(q => q.Reagent.Prototype).ToList();
            Assert.That(reagents, Does.Contain("inaprovaline"));
            Assert.That(reagents, Does.Not.Contain("Epinephrine"));
        });

        await pair.CleanReturnAsync();
    }
}
