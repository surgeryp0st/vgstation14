namespace Content.Shared._VG.Chemistry;

/// <summary>
///     Metabolises the reagents in the mob's bloodstream the vg13 way, once per Life() tick (vg13
///     <c>reagents.metabolize()</c>), instead of through SS14 metabolizer organs.
/// </summary>
[RegisterComponent]
public sealed partial class VgMetabolismComponent : Component
{
    /// <summary>
    ///     vg13 reagent <c>tick</c>: Life() ticks each reagent has been in the body. Forgotten once it's gone.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> Ticks = new();
}
