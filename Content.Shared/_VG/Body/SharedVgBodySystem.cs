using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._VG.Body;

public abstract class SharedVgBodySystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager Prototypes = default!;
    [Dependency] protected readonly IRobustRandom Random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<VgBodyComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Limbs.Count > 0)
            return;

        var proto = Prototypes.Index(ent.Comp.Prototype);
        foreach (var template in proto.Limbs)
        {
            ent.Comp.Limbs[template.Id] = new VgLimb(template);
        }

        foreach (var limb in ent.Comp.Limbs.Values)
        {
            if (limb.Parent != null && ent.Comp.Limbs.TryGetValue(limb.Parent, out var parent))
                parent.Children.Add(limb.Id);
        }
    }

    public bool TryGetLimb(Entity<VgBodyComponent?> ent, string id, [NotNullWhen(true)] out VgLimb? limb)
    {
        limb = null;
        return Resolve(ent, ref ent.Comp, false) && ent.Comp.Limbs.TryGetValue(id, out limb);
    }

    /// <summary>
    ///     Gets the limb a zone refers to (vg13 get_organ(check_zone(zone))).
    /// </summary>
    public bool TryGetLimbForZone(Entity<VgBodyComponent?> ent, string? zone, [NotNullWhen(true)] out VgLimb? limb)
    {
        return TryGetLimb(ent, VgLimbs.CheckZone(zone), out limb);
    }

    public VgLimb? GetParent(VgBodyComponent body, VgLimb limb)
    {
        return limb.Parent != null ? body.Limbs.GetValueOrDefault(limb.Parent) : null;
    }

    /// <summary>
    ///     vg13 ran_zone(): usually hits the aimed zone, otherwise strays towards the chest.
    /// </summary>
    public string RandomZone(string? zone, float probability = 90)
    {
        zone = VgLimbs.CheckZone(zone);
        if (probability >= 100)
            return zone;

        if (zone == VgLimbs.Chest)
        {
            if (Random.Prob(probability / 100))
                return VgLimbs.Chest;

            return Random.Next(1, 10) switch
            {
                <= 3 => VgLimbs.Head,
                <= 6 => VgLimbs.LeftArm,
                _ => VgLimbs.RightArm,
            };
        }

        return Random.Prob(probability * 0.75f / 100) ? zone : VgLimbs.Chest;
    }

    /// <summary>
    ///     Sums brute and burn over existing organic limbs, which is what counts towards health (vg13 updatehealth()).
    /// </summary>
    public (float Brute, float Burn) GetOrganicDamage(VgBodyComponent body)
    {
        var brute = 0f;
        var burn = 0f;
        foreach (var limb in body.Limbs.Values)
        {
            if (!limb.IsOrganic || !limb.IsExisting)
                continue;

            brute += limb.BruteDamage;
            burn += limb.BurnDamage;
        }

        return (brute, burn);
    }
}
