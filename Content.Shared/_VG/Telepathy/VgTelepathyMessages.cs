using Robust.Shared.Serialization;

namespace Content.Shared._VG.Telepathy;

[Serializable, NetSerializable]
public enum VgTelepathyUiKey : byte
{
    Key,
}

/// <summary>
///     The names the user can project to, as vg13's target list shows them.
/// </summary>
[Serializable, NetSerializable]
public sealed class VgTelepathyBuiState(List<string> names, bool setTargetsOnly) : BoundUserInterfaceState
{
    public readonly List<string> Names = names;

    /// <summary>
    ///     Mushroom people don't type a message here: their speech goes to the targets chosen (vg13 SPECIFIC/LOCAL/GLOBAL_TELEPATHY).
    /// </summary>
    public readonly bool SetTargetsOnly = setTargetsOnly;
}

[Serializable, NetSerializable]
public enum VgTelepathyMode : byte
{
    Specific,

    /// <summary>
    ///     Everyone heard before who's within speech range, whenever the mob speaks.
    /// </summary>
    Local,

    /// <summary>
    ///     Everyone heard before.
    /// </summary>
    Global,
}

/// <summary>
///     Choose who a mushroom person's speech goes to: one heard name (<see cref="Mode"/> Specific), or a whole group.
/// </summary>
[Serializable, NetSerializable]
public sealed class VgTelepathySetTargetsMessage(string? target, VgTelepathyMode mode) : BoundUserInterfaceMessage
{
    public readonly string? Target = target;
    public readonly VgTelepathyMode Mode = mode;
}

/// <summary>
///     Project a message to one heard name, or to everybody heard when <see cref="Target"/> is null (vg13's "All").
/// </summary>
[Serializable, NetSerializable]
public sealed class VgTelepathySendMessage(string? target, string message) : BoundUserInterfaceMessage
{
    public readonly string? Target = target;
    public readonly string Message = message;
}
