using Content.Server.Body.Components;
using Content.Shared._VG.Radiation;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._VG.Radiation;

/// <summary>
///     vg13 arithrazine holds off radiation's long-term symptoms. Server-side, as the bloodstream is.
/// </summary>
public sealed class VgRadiationTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private const string Arithrazine = "Arithrazine";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgRadiationComponent, VgRadiationSymptomsAttemptEvent>(OnSymptomsAttempt);
    }

    private void OnSymptomsAttempt(Entity<VgRadiationComponent> ent, ref VgRadiationSymptomsAttemptEvent args)
    {
        if (TryComp<BloodstreamComponent>(ent, out var bloodstream)
            && _solutions.ResolveSolution(ent.Owner, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals)
            && chemicals.GetTotalPrototypeQuantity(Arithrazine) > 0)
        {
            args.Blocked = true;
        }
    }
}
