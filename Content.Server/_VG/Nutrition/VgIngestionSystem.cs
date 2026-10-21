using Content.Server.Body.Systems;
using Content.Shared._VG.Nutrition;
using Content.Shared.Chemistry.Components;

namespace Content.Server._VG.Nutrition;

/// <summary>
///     vg13 eating and drinking: the food's reagents go straight into the mob's body (its bloodstream's chemicals)
///     instead of SS14's stomach, and the Life() tick metabolises them from there.
/// </summary>
public sealed class VgIngestionSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;

    public bool Applies(EntityUid target)
    {
        return HasComp<VgNutritionComponent>(target);
    }

    /// <summary>
    ///     Puts <paramref name="solution"/> in the body; false when it doesn't all fit ("you cannot eat any more").
    /// </summary>
    public bool TryIngest(EntityUid target, Solution solution)
    {
        return _bloodstream.TryAddToChemicals(target, solution);
    }
}
