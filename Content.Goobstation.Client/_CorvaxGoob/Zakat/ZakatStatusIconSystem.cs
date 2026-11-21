using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Disease;
using Content.Shared.StatusIcon.Components;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat;

/// <summary>
/// Shows «Закат» its view of the station: red = infected, blue = immune, grey = healthy organism.
/// Only the disease player sees these icons.
/// </summary>
public sealed partial class ZakatStatusIconSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<DiseaseIconPrototype> InfectedIcon = "ZakatIconInfected";
    private static readonly ProtoId<DiseaseIconPrototype> ImmuneIcon = "ZakatIconImmune";
    private static readonly ProtoId<DiseaseIconPrototype> HealthyIcon = "ZakatIconHealthy";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(ref GetStatusIconsEvent args)
    {
        if (_player.LocalEntity is not { } viewer || !TryComp<ZakatComponent>(viewer, out var zakat))
            return;

        var uid = args.Uid;
        var net = GetNetEntity(uid);
        // the disease's own creatures are in the infected set even without blood
        if (!zakat.Infected.Contains(net) && !ZakatHelpers.IsOrganic(EntityManager, uid))
            return;

        var icon = zakat.Infected.Contains(net) ? InfectedIcon
            : zakat.Immune.Contains(net) ? ImmuneIcon
            : HealthyIcon;

        if (_proto.TryIndex(icon, out var proto))
            args.StatusIcons.Add(proto);
    }
}
