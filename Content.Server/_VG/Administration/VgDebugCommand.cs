using System.Linq;
using Content.Server.Administration;
using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._VG.Administration;

/// <summary>
///     vgdebug &lt;action&gt; &lt;entity&gt; [zone]: the VG Debug verbs from the console.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class VgDebugCommand : LocalizedEntityCommands
{
    [Dependency] private readonly VgDebugSystem _debug = default!;

    private static readonly string[] Zones =
    {
        VgLimbs.Head, VgLimbs.Eyes, VgLimbs.Mouth, VgLimbs.Chest, VgLimbs.Groin, VgLimbs.LeftArm, VgLimbs.RightArm,
        VgLimbs.LeftHand, VgLimbs.RightHand, VgLimbs.LeftLeg, VgLimbs.RightLeg, VgLimbs.LeftFoot, VgLimbs.RightFoot,
    };

    public override string Command => "vgdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        var action = Enum.GetValues<VgDebugAction>().Cast<VgDebugAction?>()
            .FirstOrDefault(a => VgDebugSystem.Id(a!.Value) == args[0]);
        if (action == null)
        {
            shell.WriteError(Loc.GetString("cmd-vgdebug-bad-action", ("action", args[0])));
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net) || !EntityManager.TryGetEntity(net, out var target))
        {
            shell.WriteError(Loc.GetString("shell-invalid-entity-uid", ("uid", args[1])));
            return;
        }

        // The zone defaults to the one the caller has selected, like the verbs.
        var zone = args.Length == 3
            ? args[2]
            : EntityManager.GetComponentOrNull<VgIntentComponent>(shell.Player?.AttachedEntity)?.Zone ?? VgLimbs.Chest;

        if (_debug.Perform(null, target.Value, action.Value, zone, out var result))
            shell.WriteLine(result);
        else
            shell.WriteError(result);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Enum.GetValues<VgDebugAction>().Select(VgDebugSystem.Id),
                Loc.GetString("cmd-vgdebug-arg-action")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-vgdebug-arg-entity")),
            3 => CompletionResult.FromHintOptions(Zones, Loc.GetString("cmd-vgdebug-arg-zone")),
            _ => CompletionResult.Empty,
        };
    }
}
