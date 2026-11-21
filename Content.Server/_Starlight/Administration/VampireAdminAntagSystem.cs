using Content.Server.Administration.Managers;
using Content.Server.Antag;
using Content.Server._Starlight.GameTicking.Rules.Components;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Administration;

/// <summary>
/// Adds "Make Vampire" to the admin Antag verb menu (same place as Traitor/Heretic).
/// </summary>
public sealed class VampireAdminAntagSystem : EntitySystem
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var player = actor.PlayerSession;
        if (!_adminManager.HasAdminFlag(player, AdminFlags.Fun))
            return;

        if (!HasComp<MindContainerComponent>(args.Target)
            || !TryComp<ActorComponent>(args.Target, out var targetActor)
            || targetActor.PlayerSession is not { } targetPlayer)
            return;

        var name = Loc.GetString("admin-verb-text-make-vampire");
        args.Verbs.Add(new Verb
        {
            Text = name,
            Category = VerbCategory.Antag,
            Icon = new SpriteSpecifier.Rsi(
                new ResPath("/Textures/_Starlight/Interface/Actions/actions_vampire.rsi"),
                "fangs_extended"),
            Act = () =>
            {
                _antag.ForceMakeAntag<VampireRuleComponent>(targetPlayer, "Vampire");
            },
            Impact = LogImpact.High,
            Message = Loc.GetString("admin-verb-make-vampire"),
        });
    }
}
