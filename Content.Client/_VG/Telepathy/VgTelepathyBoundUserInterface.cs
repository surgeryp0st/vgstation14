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
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is VgTelepathyBuiState telepathy)
            _window?.SetNames(telepathy.Names);
    }
}

public sealed class VgTelepathyWindow : DefaultWindow
{
    private readonly OptionButton _targets = new();
    private readonly LineEdit _message = new() { HorizontalExpand = true };
    private readonly Label _none = new();
    private List<string> _names = new();

    public event Action<string?, string>? OnSend;

    public VgTelepathyWindow()
    {
        Title = Loc.GetString("vg-telepathy-window-title");
        MinWidth = 360;

        _message.PlaceHolder = Loc.GetString("vg-telepathy-message-placeholder");
        _message.OnTextEntered += _ => Send();
        _none.Text = Loc.GetString("vg-telepathy-none");

        var send = new Button { Text = Loc.GetString("vg-telepathy-send") };
        send.OnPressed += _ => Send();

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        row.AddChild(_message);
        row.AddChild(send);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        box.AddChild(_targets);
        box.AddChild(_none);
        box.AddChild(row);
        ContentsContainer.AddChild(box);
    }

    public void SetNames(List<string> names)
    {
        // Keep the choice while the list refreshes.
        var selected = _targets.SelectedId >= 1 && _targets.SelectedId <= _names.Count ? _names[_targets.SelectedId - 1] : null;
        _names = names;
        _targets.Clear();
        _targets.AddItem(Loc.GetString("vg-telepathy-target-all"), 0);
        for (var i = 0; i < names.Count; i++)
        {
            _targets.AddItem(names[i], i + 1);
        }

        var index = selected == null ? -1 : names.IndexOf(selected);
        _targets.SelectId(index + 1);
        _none.Visible = names.Count == 0;
    }

    private void Send()
    {
        var text = _message.Text.Trim();
        if (text.Length == 0)
            return;

        var id = _targets.SelectedId;
        OnSend?.Invoke(id >= 1 && id <= _names.Count ? _names[id - 1] : null, text);
        _message.Clear();
    }
}
