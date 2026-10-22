using Content.Server.Body.Systems;
using Content.Server.Nutrition.Components;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Species;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition;
using Content.Shared.Standing;

namespace Content.Server._VG.Species;

/// <summary>
///     vg13's SPECIES_NO_MOUTH: such a species can't eat or drink. A plant person with no mouth (a mushroom person) instead
///     dissolves food lying on its tile, at 10% of a bite a tick while standing and a whole bite lying down.
/// </summary>
public sealed class VgNoMouthSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VgSpeciesComponent, IngestionAttemptEvent>(OnIngestionAttempt);
    }

    private void OnIngestionAttempt(Entity<VgSpeciesComponent> ent, ref IngestionAttemptEvent args)
    {
        if (_species.Get(ent) is not { NoMouth: true })
            return;

        args.Cancel();
        _popup.Self(ent, Loc.GetString("vg-no-mouth"), null);
    }

    /// <summary>
    ///     Called from <see cref="VgPlasmamanSystem"/>'s Life() tick, since an entity event can have one handler per component.
    /// </summary>
    public void LifeTick(Entity<VgSpeciesComponent> ent)
    {
        if (_species.Get(ent) is not { NoMouth: true, IsPlant: true })
            return;

        Absorb(ent, TryComp<StandingStateComponent>(ent, out var standing) && !standing.Standing);
    }

    /// <summary>
    ///     One tick of absorbing the food under <paramref name="ent"/>: a bite if it's lying down, a tenth of one if not.
    /// </summary>
    public void Absorb(EntityUid ent, bool lying)
    {
        var fraction = lying ? 1f : 0.1f;
        var absorbed = false;

        foreach (var food in _lookup.GetEntitiesInRange<FoodComponent>(Transform(ent).Coordinates, 0.5f))
        {
            if (!_solutions.TryGetSolution(food.Owner, food.Comp.Solution, out var soln, out var solution))
                continue;

            var bite = food.Comp.TransferAmount ?? solution.Volume;
            var take = FixedPoint2.Min(FixedPoint2.New(bite.Float() * fraction), solution.Volume);
            if (take <= 0)
                continue;

            if (!absorbed)
            {
                // vg13 words the first food by how full the mob is
                var fullness = _nutrition.GetNutrition(ent);
                var message = fullness switch
                {
                    <= 50 => "vg-no-mouth-absorb-urgency",
                    <= 150 => "vg-no-mouth-absorb-purpose",
                    <= 350 => "vg-no-mouth-absorb",
                    _ => "vg-no-mouth-absorb-lethargy",
                };
                _popup.Self(ent, Loc.GetString(message), null, Content.Shared.Popups.PopupType.Small);
            }

            absorbed = true;
            _bloodstream.TryAddToChemicals(ent, _solutions.SplitSolution(soln.Value, take));
            if (solution.Volume <= 0)
                QueueDel(food);
        }
    }
}
