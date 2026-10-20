using Content.Shared._VG.Input;
using Robust.Shared.Input;

namespace Content.Client._VG.Input;

public static class VgContexts
{
    /// <summary>
    ///     Adds the vgstation13 hotkeys to the "human" input context.
    /// </summary>
    public static void Setup(IInputContextContainer contexts)
    {
        var human = contexts.GetContext("human");
        foreach (var function in AllFunctions())
        {
            human.AddFunction(function);
        }
    }

    /// <summary>
    ///     Every vg hotkey, in the order the key bind options list them.
    /// </summary>
    public static IEnumerable<BoundKeyFunction> AllFunctions()
    {
        foreach (var (function, _) in VgKeyFunctions.Intents)
        {
            yield return function;
        }

        yield return VgKeyFunctions.VgIntentCycle;

        foreach (var (function, _) in VgKeyFunctions.Zones)
        {
            yield return function;
        }
    }
}
