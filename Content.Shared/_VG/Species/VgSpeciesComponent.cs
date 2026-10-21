using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Species;

/// <summary>
///     Which vg13 species this mob is. The species' numbers (<see cref="VgSpeciesPrototype"/>) are read by the vg
///     systems that use them: unarmed attacks, movement speed and the body's organs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgSpeciesComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VgSpeciesPrototype> Species;
}
