using Content.Shared.FixedPoint;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     One thing a reagent does to a mob each Life() tick; the data form of a line in vg13's <c>on_mob_life</c>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class VgReagentEffect
{
    /// <summary>
    ///     Only applies from this Life() tick the reagent has been in the body (vg13's <c>switch(tick)</c> ranges).
    /// </summary>
    [DataField]
    public int MinTick;

    /// <summary>
    ///     Only applies up to this tick. 0 for no limit.
    /// </summary>
    [DataField]
    public int MaxTick;

    /// <summary>
    ///     vg13 <c>if(prob(x))</c>: percent chance the effect happens on a given tick.
    /// </summary>
    [DataField]
    public float Chance = 100;

    public bool InTickRange(int tick)
    {
        return tick >= MinTick && (MaxTick <= 0 || tick <= MaxTick);
    }

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
