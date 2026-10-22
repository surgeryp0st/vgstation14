using Content.Shared._VG.Telepathy;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._VG.Telepathy;

[UsedImplicitly]
public sealed class VgTelepathyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private VgTelepathyWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<VgTelepathyWindow>();
        _window.OnSend += (target, message) => SendMessage(new VgTelepathySendMessage(target, message));
        _window.OnSetTargets += (target, mode) => SendMessage(new VgTelepathySetTargetsMessage(target, mode));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is VgTelepathyBuiState telepathy)
            _window?.SetNames(telepathy.Names, telepathy.SetTargetsOnly);
    }
}

public sealed class VgTelepathyWindow : DefaultWindow
{
    private readonly OptionButton _targets = new();
    private readonly LineEdit _message = new() { HorizontalExpand = true };
    private readonly Label _none = new();
    private readonly Button _send = new();
    private readonly BoxContainer _row = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal };

    // What each entry of the dropdown stands for: a name, or one of the groups.
    private List<(string? Name, VgTelepathyMode Mode)> _entries = new();
    private bool _setTargetsOnly;

    public event Action<string?, string>? OnSend;
    public event Action<string?, VgTelepathyMode>? OnSetTargets;

    public VgTelepathyWindow()
    {
        Title = Loc.GetString("vg-telepathy-window-title");
        MinWidth = 360;

        _message.PlaceHolder = Loc.GetString("vg-telepathy-message-placeholder");
        _message.OnTextEntered += _ => Send();
        _none.Text = Loc.GetString("vg-telepathy-none");
        _send.OnPressed += _ => Send();

        _row.AddChild(_message);
        _row.AddChild(_send);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        box.AddChild(_targets);
        box.AddChild(_none);
        box.AddChild(_row);
        ContentsContainer.AddChild(box);

        _targets.OnItemSelected += args =>
        {
            _targets.SelectId(args.Id);
            if (_setTargetsOnly)
                Send();
        };
    }

    public void SetNames(List<string> names, bool setTargetsOnly)
    {
        // Keep the choice while the list refreshes.
        var selected = _targets.SelectedId >= 0 && _targets.SelectedId < _entries.Count ? _entries[_targets.SelectedId] : default;
        _setTargetsOnly = setTargetsOnly;
        _entries = new();
        _targets.Clear();

        if (setTargetsOnly)
            Add(Loc.GetString("vg-telepathy-target-local"), null, VgTelepathyMode.Local);

        Add(Loc.GetString("vg-telepathy-target-all"), null, VgTelepathyMode.Global);
        foreach (var name in names)
        {
            Add(name, name, VgTelepathyMode.Specific);
        }

        var index = _entries.IndexOf(selected);
        _targets.SelectId(index < 0 ? 0 : index);
        _none.Visible = names.Count == 0;
        _message.Visible = !setTargetsOnly;
        _send.Text = Loc.GetString(setTargetsOnly ? "vg-telepathy-set-targets" : "vg-telepathy-send");
    }

    private void Add(string label, string? name, VgTelepathyMode mode)
    {
        _targets.AddItem(label, _entries.Count);
        _entries.Add((name, mode));
    }

    private void Send()
    {
        var id = _targets.SelectedId;
        if (id < 0 || id >= _entries.Count)
            return;

        var (name, mode) = _entries[id];
        if (_setTargetsOnly)
        {
            OnSetTargets?.Invoke(name, mode);
            return;
        }

        var text = _message.Text.Trim();
        if (text.Length == 0)
            return;

        OnSend?.Invoke(name, text);
        _message.Clear();
    }
}
