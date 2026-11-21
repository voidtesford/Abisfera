// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Damage.Components;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted.Components;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Traits.Assorted.Systems;

/// <summary>Adds a self-examine verb that reports selected damage types and groups.</summary>
public sealed class SelfAwareSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SelfAwareComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(Entity<SelfAwareComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (args.User != ent.Owner || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new ExamineVerb
        {
            Act = () => _examine.SendExamineTooltip(user, ent, BuildMarkup(ent), getVerbs: false, centerAtCursor: false),
            Text = Loc.GetString("self-aware-verb"),
            Message = Loc.GetString("self-aware-verb-desc"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/dot.svg.192dpi.png")),
        });
    }

    private FormattedMessage BuildMarkup(Entity<SelfAwareComponent> ent)
    {
        var msg = new FormattedMessage();
        if (!TryComp<DamageableComponent>(ent, out var damage))
            return msg;

        _thresholds.TryGetThresholdForState(ent, MobState.Critical, out var crit);
        var critVal = crit ?? FixedPoint2.New(100);

        foreach (var type in ent.Comp.AnalyzableTypes)
        {
            var key = type.ToString();
            if (!damage.Damage.DamageDict.TryGetValue(key, out var amount) || amount <= 0)
                continue;
            var name = _proto.TryIndex(type, out var proto) ? proto.LocalizedName : key;
            msg.AddMarkupOrThrow(Loc.GetString("self-aware-type", ("type", name), ("amount", amount)));
            msg.PushNewline();
        }

        foreach (var group in ent.Comp.DetectableGroups)
        {
            var key = group.ToString();
            if (!damage.DamagePerGroup.TryGetValue(key, out var amount) || amount <= 0)
                continue;
            var ratio = critVal <= 0 ? 0 : (amount / critVal).Float();
            var tier = 0;
            foreach (var t in ent.Comp.Thresholds)
            {
                if (ratio >= t.Float())
                    tier++;
            }

            if (tier <= 0)
                continue;

            var name = _proto.TryIndex(group, out var proto) ? proto.LocalizedName : key;
            msg.AddMarkupOrThrow(Loc.GetString($"self-aware-group-{tier}", ("group", name)));
            msg.PushNewline();
        }

        if (msg.IsEmpty)
            msg.AddMarkupOrThrow(Loc.GetString("self-aware-none"));
        return msg;
    }
}
