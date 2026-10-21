using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Light.Components;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared._VG.Power;
using Content.Server.Popups;
using Content.Shared.Access.Systems;
using Content.Shared.APC;
using Content.Shared.Popups;
using Content.Shared.Doors.Components;
using Robust.Shared.Timing;

namespace Content.Server._VG.Power;

/// <summary>
///     vg13 APC channels (apc.dm). Everything powered through an APC's network is its area: each machine is on the
///     equipment, lighting or environment channel, and a channel that is off takes its machines' power away. The APC's
///     battery decides when channels cut themselves (below 30% with no charging in sight equipment goes, below 15%
///     lighting too, and with an empty battery everything).
/// </summary>
public sealed class VgApcChannelSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgApcChannelsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VgApcChannelsComponent, VgApcSetChannelMessage>(OnSetChannel);
    }

    private void OnSetChannel(Entity<VgApcChannelsComponent> ent, ref VgApcSetChannelMessage args)
    {
        if (!_accessReader.IsAllowed(args.Actor, ent))
        {
            _popup.PopupCursor(Loc.GetString("apc-component-insufficient-access"), args.Actor, PopupType.Medium);
            return;
        }

        var state = args.Mode switch
        {
            VgApcChannelMode.Off => VgApcChannelState.Off,
            VgApcChannelMode.On => VgApcChannelState.On,
            _ => VgApcChannelState.AutoOn,
        };
        SetChannel(ent, args.Channel, state);
    }

    private void OnShutdown(Entity<VgApcChannelsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<ApcComponent>(ent, out var apc))
            SetReceivers(apc, null);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Tick;

        // Every APC gets the vg13 channels.
        var apcs = EntityQueryEnumerator<ApcComponent>();
        while (apcs.MoveNext(out var apcUid, out _))
        {
            EnsureComp<VgApcChannelsComponent>(apcUid);
        }

        var query = EntityQueryEnumerator<VgApcChannelsComponent, ApcComponent, PowerNetworkBatteryComponent>();
        while (query.MoveNext(out var uid, out var channels, out var apc, out var battery))
        {
            AutoSet(uid, channels, apc, battery);
            SetReceivers(apc, channels);
        }
    }

    /// <summary>
    ///     vg13 process_autoset(): the channels follow the battery.
    /// </summary>
    private void AutoSet(EntityUid uid, VgApcChannelsComponent channels, ApcComponent apc, PowerNetworkBatteryComponent battery)
    {
        var storage = battery.NetworkBattery.CurrentStorage;
        var percent = battery.NetworkBattery.Capacity > 0 ? storage / battery.NetworkBattery.Capacity * 100 : 0;

        var charging = apc.LastChargeState is ApcChargeState.Charging or ApcChargeState.Full;
        if (charging && channels.LongTermPower < 10)
            channels.LongTermPower++;
        else if (channels.LongTermPower > -10)
            channels.LongTermPower -= 2;

        // vg13: 0 turns on-channels auto-off, 1 turns auto-off channels auto-on, 2 turns auto-on channels auto-off.
        VgApcChannelState equipment, lighting, environment;
        if (!apc.MainBreakerEnabled || storage <= 0)
        {
            (equipment, lighting, environment) = (AutoSet(channels.Equipment, 0), AutoSet(channels.Lighting, 0), AutoSet(channels.Environment, 0));
        }
        else if (percent < 15 && channels.LongTermPower < 0)
        {
            (equipment, lighting, environment) = (AutoSet(channels.Equipment, 2), AutoSet(channels.Lighting, 2), AutoSet(channels.Environment, 1));
        }
        else if (percent < 30 && channels.LongTermPower < 0)
        {
            (equipment, lighting, environment) = (AutoSet(channels.Equipment, 2), AutoSet(channels.Lighting, 1), AutoSet(channels.Environment, 1));
        }
        else
        {
            (equipment, lighting, environment) = (AutoSet(channels.Equipment, 1), AutoSet(channels.Lighting, 1), AutoSet(channels.Environment, 1));
        }

        if (equipment != channels.Equipment || lighting != channels.Lighting || environment != channels.Environment)
        {
            channels.Equipment = equipment;
            channels.Lighting = lighting;
            channels.Environment = environment;
            Dirty(uid, channels);
        }
    }

    private static VgApcChannelState AutoSet(VgApcChannelState value, int on)
    {
        switch (on)
        {
            case 0:
                if (value is VgApcChannelState.On or VgApcChannelState.AutoOn)
                    return VgApcChannelState.AutoOff;
                break;
            case 1:
                if (value == VgApcChannelState.AutoOff)
                    return VgApcChannelState.AutoOn;
                break;
            case 2:
                if (value == VgApcChannelState.AutoOn)
                    return VgApcChannelState.AutoOff;
                break;
        }

        return value;
    }

    public static bool IsOn(VgApcChannelState state)
    {
        return state is VgApcChannelState.On or VgApcChannelState.AutoOn;
    }

    /// <summary>
    ///     Sets a channel by hand (vg13's APC interface): off, on, or auto.
    /// </summary>
    public void SetChannel(Entity<VgApcChannelsComponent> apc, VgPowerChannel channel, VgApcChannelState state)
    {
        switch (channel)
        {
            case VgPowerChannel.Equipment:
                apc.Comp.Equipment = state;
                break;
            case VgPowerChannel.Lighting:
                apc.Comp.Lighting = state;
                break;
            case VgPowerChannel.Environment:
                apc.Comp.Environment = state;
                break;
        }

        Dirty(apc);
        if (TryComp<ApcComponent>(apc, out var apcComp))
            SetReceivers(apcComp, apc.Comp);
    }

    public VgApcChannelState GetState(VgApcChannelsComponent channels, VgPowerChannel channel)
    {
        return channel switch
        {
            VgPowerChannel.Lighting => channels.Lighting,
            VgPowerChannel.Environment => channels.Environment,
            _ => channels.Equipment,
        };
    }

    /// <summary>
    ///     Which channel a machine uses: its override, otherwise by what it is (vg13 power_channel).
    /// </summary>
    public VgPowerChannel GetChannel(EntityUid uid)
    {
        if (TryComp<VgPowerChannelComponent>(uid, out var overridden))
            return overridden.Channel;

        if (HasComp<PoweredLightComponent>(uid))
            return VgPowerChannel.Lighting;

        if (HasComp<DoorComponent>(uid) || HasComp<GasVentPumpComponent>(uid) || HasComp<GasVentScrubberComponent>(uid) || HasComp<AirAlarmComponent>(uid))
            return VgPowerChannel.Environment;

        return VgPowerChannel.Equipment;
    }

    /// <summary>
    ///     Cuts or restores the power of every machine on the APC's network. Null channels restore everything.
    /// </summary>
    private void SetReceivers(ApcComponent apc, VgApcChannelsComponent? channels)
    {
        if (apc.Net is not ApcNet net)
            return;

        foreach (var provider in net.Providers)
        {
            foreach (var receiver in provider.LinkedReceivers)
            {
                var cut = channels != null && !IsOn(GetState(channels, GetChannel(receiver.Owner)));
                SetCut(receiver, cut);
            }
        }
    }

    private static void SetCut(ApcPowerReceiverComponent receiver, bool cut)
    {
        if (receiver.VgChannelCut == cut)
            return;

        receiver.VgChannelCut = cut;
        receiver.Recalculate = true;

        // A machine whose channel is off draws nothing.
        if (cut)
        {
            receiver.VgSavedLoad = receiver.Load;
            receiver.Load = 0;
        }
        else
        {
            receiver.Load = receiver.VgSavedLoad;
        }
    }
}
