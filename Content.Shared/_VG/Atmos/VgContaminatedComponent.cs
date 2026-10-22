using Robust.Shared.GameStates;

namespace Content.Shared._VG.Atmos;

/// <summary>
///     vg13 contaminated: plasma has got into it (code/modules/ZAS/Plasma.dm). Whoever has it on them takes a little
///     toxin damage every Life() tick until it's bleached clean.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgContaminatedComponent : Component;
