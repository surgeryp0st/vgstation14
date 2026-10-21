namespace Content.Server.Power.Components;

public sealed partial class ApcPowerReceiverComponent
{
    /// <summary>
    ///     VG: the machine's vg13 power channel on its APC is off, so it gets no power and draws none.
    /// </summary>
    [ViewVariables]
    public bool VgChannelCut;

    /// <summary>
    ///     VG: the load to go back to when the channel comes back on.
    /// </summary>
    public float VgSavedLoad;
}
