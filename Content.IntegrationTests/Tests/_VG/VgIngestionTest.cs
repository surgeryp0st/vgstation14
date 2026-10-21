using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Body.Components;
using Content.Shared._VG.Nutrition;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>
///     vg13 eating: food reagents go into the bloodstream, not a stomach.
/// </summary>
[TestFixture]
public sealed class VgIngestionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task EatingPutsFoodInTheBloodstream()
    {
        await AddAtmosphere();
        await PlaceInHands("FoodBreadPlainSlice");
        await UseInHand();
        await RunSeconds(5);

        var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);
        SEntMan.System<SharedSolutionContainerSystem>()
            .ResolveSolution(SPlayer, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals);
        Assert.That(chemicals!.Volume.Float(), Is.GreaterThan(0), "the bread went into the body");
    }
}
