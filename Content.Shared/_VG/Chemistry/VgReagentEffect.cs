using Content.Shared.FixedPoint;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     One thing a reagent does to a mob each Life() tick; the data form of a line in vg13's <c>on_mob_life</c>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class VgReagentEffect
{
    /// <summary>
    ///     Only applies from this Life() tick the reagent has been in the body (vg13's <c>switch(tick)</c> ranges,
    ///     copied as they are: like vg13's, the first tick is 0).
    /// </summary>
    [DataField]
    public int MinTick;

    /// <summary>
    ///     Only applies up to this tick; no limit when unset.
    /// </summary>
    [DataField]
    public int? MaxTick;

    /// <summary>
    ///     vg13 <c>if(prob(x))</c>: percent chance the effect happens on a given tick.
    /// </summary>
    [DataField]
    public float Chance = 100;

    /// <summary>
    ///     vg13 <c>alien == IS_VOX</c> and the like: only mobs of these vg species.
    /// </summary>
    [DataField]
    public List<string>? OnlySpecies;

    /// <summary>
    ///     Everything except these vg species.
    /// </summary>
    [DataField]
    public List<string>? ExceptSpecies;

    public bool InTickRange(int tick)
    {
        return tick >= MinTick && (MaxTick == null || tick <= MaxTick);
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
    Action<string, float> RemoveReagent,
    IReadOnlyList<string> Contents)
{
    public T System<T>() where T : EntitySystem
    {
        return EntityManager.System<T>();
    }
}
