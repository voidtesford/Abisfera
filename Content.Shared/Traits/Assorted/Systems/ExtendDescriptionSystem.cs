// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Examine;
using Content.Shared.Traits.Assorted.Components;

namespace Content.Shared.Traits.Assorted.Systems;

public sealed class ExtendDescriptionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExtendDescriptionComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, ExtendDescriptionComponent component, ExaminedEvent args)
    {
        foreach (var desc in component.DescriptionList)
        {
            if (!args.IsInDetailsRange && desc.RequireDetailRange)
                continue;

            // Requirements on flavour text are unused without a full character profile; show the main description.
            var description = desc.Description;
            if (string.IsNullOrEmpty(description))
                continue;

            args.PushMarkup($"[font size={desc.FontSize}][color={desc.Color}]{Loc.GetString(description, ("entity", uid))}[/color][/font]");
        }
    }
}
