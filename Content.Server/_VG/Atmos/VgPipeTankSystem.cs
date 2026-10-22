using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer;
using Content.Shared.Atmos;

namespace Content.Server._VG.Atmos;

/// <summary>
///     Fills vg13 pressure tanks with their starting gas. The pipe network a node belongs to only exists after the node
///     groups have formed, so the fill waits for that and then merges the gas in.
/// </summary>
public sealed class VgPipeTankSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VgPipeTankComponent, NodeContainerComponent>();
        while (query.MoveNext(out _, out var tank, out var nodes))
        {
            if (tank.Filled)
                continue;

            PipeNode? pipe = null;
            foreach (var node in nodes.Nodes.Values)
            {
                if (node is PipeNode p)
                {
                    pipe = p;
                    break;
                }
            }

            // Not in a group yet.
            if (pipe?.NodeGroup == null)
                continue;

            tank.Filled = true;
            if (tank.Gases.Count == 0)
                continue;

            // vg13: moles = pressure * volume / (R * T), split by the mixture's fractions.
            var total = tank.Pressure * pipe.Volume / (Atmospherics.R * tank.Temperature);
            var fill = new GasMixture(pipe.Volume) { Temperature = tank.Temperature };
            var fractions = 0f;
            foreach (var f in tank.Gases.Values)
                fractions += f;

            foreach (var (gas, fraction) in tank.Gases)
                fill.AdjustMoles(gas, total * fraction / fractions);

            _atmosphere.Merge(pipe.Air, fill);
        }
    }
}
