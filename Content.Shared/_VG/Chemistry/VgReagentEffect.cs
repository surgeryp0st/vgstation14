using Content.Shared.FixedPoint;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     One thing a reagent does to a mob each Life() tick; the data form of a line in vg13's <c>on_mob_life</c>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class VgReagentEffect
{
    public abstract void Apply(VgReagentArgs args);
}

/// <summary>
///     What an effect is applied to.
/// </summary>
public readonly record struct VgReagentArgs(
    IEntityManager EntityManager,
    EntityUid Target,
    string Reagent,
    FixedPoint2 Volume,
    int Tick,
    Action<string, float> RemoveReagent)
{
    public T System<T>() where T : EntitySystem
    {
        return EntityManager.System<T>();
    }
}
