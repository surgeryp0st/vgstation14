using Robust.Shared.Input;

namespace Content.Shared._VG.Input;

/// <summary>
///     vgstation13 hotkeys. Defaults in keybinds.yml follow vg13's skin.dmf: Ctrl+1-4 for intents, Insert to cycle,
///     Ctrl+Numpad for zones and Alt+Numpad for hands and feet.
/// </summary>
[KeyFunctions]
public static class VgKeyFunctions
{
    public static readonly BoundKeyFunction VgIntentHelp = "VgIntentHelp";
    public static readonly BoundKeyFunction VgIntentDisarm = "VgIntentDisarm";
    public static readonly BoundKeyFunction VgIntentGrab = "VgIntentGrab";
    public static readonly BoundKeyFunction VgIntentHarm = "VgIntentHarm";
    public static readonly BoundKeyFunction VgIntentCycle = "VgIntentCycle";

    public static readonly BoundKeyFunction VgZoneHead = "VgZoneHead";
    public static readonly BoundKeyFunction VgZoneEyes = "VgZoneEyes";
    public static readonly BoundKeyFunction VgZoneMouth = "VgZoneMouth";
    public static readonly BoundKeyFunction VgZoneChest = "VgZoneChest";
    public static readonly BoundKeyFunction VgZoneGroin = "VgZoneGroin";
    public static readonly BoundKeyFunction VgZoneLeftArm = "VgZoneLeftArm";
    public static readonly BoundKeyFunction VgZoneRightArm = "VgZoneRightArm";
    public static readonly BoundKeyFunction VgZoneLeftHand = "VgZoneLeftHand";
    public static readonly BoundKeyFunction VgZoneRightHand = "VgZoneRightHand";
    public static readonly BoundKeyFunction VgZoneLeftLeg = "VgZoneLeftLeg";
    public static readonly BoundKeyFunction VgZoneRightLeg = "VgZoneRightLeg";
    public static readonly BoundKeyFunction VgZoneLeftFoot = "VgZoneLeftFoot";
    public static readonly BoundKeyFunction VgZoneRightFoot = "VgZoneRightFoot";

    public static readonly (BoundKeyFunction Function, string Zone)[] Zones =
    {
        (VgZoneHead, "head"),
        (VgZoneEyes, "eyes"),
        (VgZoneMouth, "mouth"),
        (VgZoneChest, "chest"),
        (VgZoneGroin, "groin"),
        (VgZoneLeftArm, "l_arm"),
        (VgZoneRightArm, "r_arm"),
        (VgZoneLeftHand, "l_hand"),
        (VgZoneRightHand, "r_hand"),
        (VgZoneLeftLeg, "l_leg"),
        (VgZoneRightLeg, "r_leg"),
        (VgZoneLeftFoot, "l_foot"),
        (VgZoneRightFoot, "r_foot"),
    };

    public static readonly (BoundKeyFunction Function, Combat.VgIntent Intent)[] Intents =
    {
        (VgIntentHelp, Combat.VgIntent.Help),
        (VgIntentDisarm, Combat.VgIntent.Disarm),
        (VgIntentGrab, Combat.VgIntent.Grab),
        (VgIntentHarm, Combat.VgIntent.Harm),
    };
}
