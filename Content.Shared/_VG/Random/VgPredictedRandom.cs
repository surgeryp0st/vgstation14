using Robust.Shared.Timing;

namespace Content.Shared._VG.Random;

/// <summary>
///     Randomness that comes out the same on the server and the predicting client.
/// </summary>
public static class VgPredictedRandom
{
    /// <summary>
    ///     A <see cref="System.Random"/> seeded from the tick and some ids (usually <see cref="NetEntity"/> ids and a salt
    ///     telling different rolls apart). Not <see cref="HashCode.Combine{T1,T2}"/>: it's seeded per process.
    /// </summary>
    public static System.Random Create(GameTick tick, params int[] ids)
    {
        var seed = (int) tick.Value;
        foreach (var id in ids)
        {
            seed = unchecked(seed * 397 ^ id);
        }

        return new System.Random(seed);
    }

    /// <summary>
    ///     DM prob(): a percent chance, where anything over 100 always happens.
    /// </summary>
    public static bool Prob(this System.Random random, float percent)
    {
        return random.NextDouble() * 100 < percent;
    }
}
