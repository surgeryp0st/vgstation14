using Content.Shared._VG.Power;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.Client.Power.APC.UI;

/// <summary>
///     The vg13 power channels on the APC window: for each of equipment, lighting and environment, buttons to switch it
///     off, on or to auto, and what it is doing now.
/// </summary>
public sealed partial class ApcMenu
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public event Action<VgPowerChannel, VgApcChannelMode>? OnVgChannel;

    private EntityUid? _vgApc;
    private readonly Dictionary<VgPowerChannel, Label> _vgStatus = new();
    private readonly List<Button> _vgButtons = new();

    private void BuildVgChannels()
    {
        foreach (var channel in new[] { VgPowerChannel.Equipment, VgPowerChannel.Lighting, VgPowerChannel.Environment })
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new Label { Text = Loc.GetString($"vg-apc-channel-{channel.ToString().ToLowerInvariant()}"), MinWidth = 100 });

            foreach (var mode in new[] { VgApcChannelMode.Off, VgApcChannelMode.On, VgApcChannelMode.Auto })
            {
                var captured = (channel, mode);
                var button = new Button { Text = Loc.GetString($"vg-apc-mode-{mode.ToString().ToLowerInvariant()}"), MinWidth = 50 };
                button.OnPressed += _ => OnVgChannel?.Invoke(captured.channel, captured.mode);
                _vgButtons.Add(button);
                row.AddChild(button);
            }

            var status = new Label { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            _vgStatus[channel] = status;
            row.AddChild(status);
            VgChannels.AddChild(row);
        }
    }

    public void SetVgApc(EntityUid apc)
    {
        _vgApc = apc;
    }

    public void SetVgAccess(bool hasAccess)
    {
        foreach (var button in _vgButtons)
        {
            button.Disabled = !hasAccess;
        }
    }

    private void UpdateVgChannels()
    {
        if (_vgApc is not { } apc || !_entMan.TryGetComponent(apc, out VgApcChannelsComponent? channels))
            return;

        _vgStatus[VgPowerChannel.Equipment].Text = StateText(channels.Equipment);
        _vgStatus[VgPowerChannel.Lighting].Text = StateText(channels.Lighting);
        _vgStatus[VgPowerChannel.Environment].Text = StateText(channels.Environment);
    }

    private static string StateText(VgApcChannelState state)
    {
        return Loc.GetString($"vg-apc-state-{state.ToString().ToLowerInvariant()}");
    }
}
