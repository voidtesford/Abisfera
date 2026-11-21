// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Server.Traits.Assorted;

public sealed class LiquorLifelineSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LiquorLifelineComponent, ComponentInit>(OnSpawn);
    }

    private void OnSpawn(Entity<LiquorLifelineComponent> entity, ref ComponentInit args)
    {
        if (!TryComp<BodyComponent>(entity, out var body))
            return;

        if (!_body.TryGetBodyOrganEntityComps<MetabolizerComponent>((entity, body), out var metabolizers))
            return;

        foreach (var metabolizer in metabolizers)
        {
            if (metabolizer.Comp1.MetabolizerTypes is null || metabolizer.Comp1.MetabolismGroups is null)
                continue;

            foreach (var metabolismGroup in metabolizer.Comp1.MetabolismGroups)
            {
                if (metabolismGroup.Id == "Alcohol")
                    metabolizer.Comp1.MetabolizerTypes.Add("LiquorLifeline");
            }
        }
    }
}
