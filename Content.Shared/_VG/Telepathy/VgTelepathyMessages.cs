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
public sealed class VgTelepathyBuiState(List<string> names) : BoundUserInterfaceState
{
    public readonly List<string> Names = names;
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
