using Abisfera.AnimEditor.Model;

namespace Abisfera.AnimEditor.Rendering;

public enum Outfit : byte
{
    /// <summary>Bare body (underwear-less), the species sprites only.</summary>
    None,
    /// <summary>Grey jumpsuit and black shoes (a plain crew member, like the preview's "human").</summary>
    Jumpsuit,
    /// <summary>Gilbert's loadout (helmet, gas mask, armour, webbing, duffel, M8 shotgun).</summary>
    Gilbert,
}

/// <summary>How one puppet looks: species, outfit and colours.</summary>
public sealed record Appearance(string Species, Outfit Outfit, string? Skin = null, bool HeldItem = true, bool Hair = true, string Eyes = "#3c5aa0")
{
    public static Appearance Default(int actor) => actor == 0
        ? new Appearance("Human", Outfit.Gilbert, Eyes: "#c81414")
        : new Appearance("Human", Outfit.Jumpsuit);
}

/// <summary>
/// Builds the visible sprite layers of a humanoid in the game's draw order and layer-map keys (body parts from the
/// species' base sprites, required default markings, clothing / held items of the outfit), each a resolved RSI frame
/// with its layer colour applied. These are what ProcAnimPuppet splits, exactly as the client does for a real mob.
/// </summary>
public static class AppearanceBuilder
{
    /// <summary>Draw order of the humanoid sprite (base.yml layers + clothing bookmarks + in-hands), as preview.py.</summary>
    public static readonly string[] DrawOrder =
    {
        "Groin", "Chest", "Head", "Snout", "Eyes", "RArm", "LArm", "RLeg", "LLeg", "Underwear", "Undershirt", "jumpsuit",
        "LFoot", "RFoot", "LHand", "RHand", "Handcuffs", "Face", "gloves", "shoes", "ears", "eyes", "id", "outerClothing",
        "belt", "back", "neck", "FacialHair", "HeadSide", "maskalt", "Hair", "mask", "HeadTop", "Tail", "head", "pocket1",
        "pocket2", "inhand-left", "back-slung", "inhand-right",
    };

    private static readonly HashSet<string> SkinLayers = new()
        { "Groin", "Chest", "Head", "RArm", "LArm", "RLeg", "LLeg", "LFoot", "RFoot", "LHand", "RHand", "Snout", "HeadTop", "HeadSide", "Tail" };

    public const string Shotgun = "_CorvaxGoob/Objects/Weapons/Guns/Shotguns/m8_shotgun.rsi";

    public static Color SkinOf(GameRepo repo, Appearance app)
    {
        if (app.Skin is { } hex && Color.TryFromHex(hex, out var c))
            return c;
        if (repo.Species.TryGetValue(app.Species, out var sp) && sp.DefaultSkin is { } d)
            return d;
        return Color.FromHex("#ffe1cc");
    }

    /// <param name="dir">RSI direction: 0 S, 1 N, 2 E, 3 W.</param>
    /// <param name="wield">The scene wields its item (the level two-handed in-hand frame).</param>
    public static List<(string? Key, Frame Frame)> Layers(GameRepo repo, RsiLoader rsi, Appearance app, int dir, bool wield)
    {
        var layers = new Dictionary<string, List<Frame>>();
        void Add(string key, Frame? f)
        {
            if (f == null)
                return;
            if (!layers.TryGetValue(key, out var l))
                layers[key] = l = new List<Frame>();
            l.Add(f);
        }

        var skin = SkinOf(repo, app);
        if (!repo.Species.TryGetValue(app.Species, out var sp))
            repo.Species.TryGetValue("Human", out sp);
        if (sp != null)
        {
            foreach (var (key, sr) in sp.Base)
            {
                var f = rsi.State(sr.Rsi, sr.State, dir);
                if (f == null)
                    continue;
                var col = key == "Eyes" ? Color.FromHex(app.Eyes, Color.Black) : SkinLayers.Contains(key) ? skin : Color.White;
                Add(key, RsiLoader.Tint(f, col));
            }

            // Required default markings (a vulpkanin's tail, ...): drawn with the skin colour on their body part's slot,
            // without a layer-map key (like the game's marking layers, the puppet cuts them by the body masks).
            foreach (var id in sp.DefaultMarkings)
            {
                if (!repo.Markings.TryGetValue(id, out var mk))
                    continue;
                foreach (var s in mk.Sprites)
                    Add("marking:" + mk.BodyPart, rsi.State(s.Rsi, s.State, dir) is { } f ? RsiLoader.Tint(f, skin) : null);
            }
        }

        if (app.Hair && app.Outfit != Outfit.Gilbert && sp?.Base.ContainsKey("Hair") != false
            && rsi.State("Mobs/Customization/human_hair.rsi", "business3", dir) is { } hair)
            Add("Hair", RsiLoader.Tint(hair, Color.FromHex("#5a3c1e")));

        switch (app.Outfit)
        {
            case Outfit.Jumpsuit:
                Add("jumpsuit", Tinted(rsi.State("Clothing/Uniforms/Jumpsuit/color.rsi", "equipped-INNERCLOTHING", dir), "#b3b3b3"));
                if (Tinted(rsi.State("Clothing/Shoes/color.rsi", "equipped-FEET", dir), "#1d1d1d") is { } shoes)
                {
                    if (rsi.State("Clothing/Shoes/color.rsi", "soles-equipped-FEET", dir) is { } soles)
                        RsiLoader.Over(shoes, soles);
                    Add("shoes", shoes);
                }

                break;
            case Outfit.Gilbert:
                foreach (var (eid, st, slot) in new[]
                         {
                             ("ClothingShoesBootsShogkk", "equipped-FEET", "shoes"), ("ClothingHandsGlovesCombat", "equipped-HAND", "gloves"),
                             ("ClothingOuterArmorBulletproof", "equipped-OUTERCLOTHING", "outerClothing"),
                             ("ClothingBeltShogkkWebbingFilled", "equipped-BELT", "belt"),
                             ("ClothingBackpackDuffelMilitaryShogkkFilled", "equipped-BACKPACK", "back"),
                             ("ClothingMaskGasSwat", "equipped-MASK", "mask"), ("ClothingHeadHelmetShogkk", "equipped-HELMET", "head"),
                             ("ClothingNeckDogtagShogkk", "equipped-NECK", "neck"),
                         })
                {
                    if (repo.EntitySprite(eid) is { } path)
                        Add(slot, rsi.State(path, st, dir));
                }

                var suit = new Frame(32, 32, new byte[32 * 32 * 4]);
                foreach (var (ls, col) in new[] { ("equipped-base-bottom", "#1c1c22"), ("equipped-base-top-jumpsuit-tac", "#1c1c22"), ("equipped-decor-contractor", "#34343c") })
                    if (Tinted(rsi.State("_NF/Clothing/Uniforms/Jumpsuit/military.rsi", ls, dir), col) is { } part && part.W == 32 && part.H == 32)
                        RsiLoader.Over(suit, part);
                Add("jumpsuit", suit);
                if (app.HeldItem)
                {
                    Add("inhand-right", rsi.State(Shotgun, wield ? "wielded-inhand-right" : "inhand-right", dir));
                    Add("back-slung", rsi.State(Shotgun, "equipped-BACKPACK", dir));
                }

                break;
        }

        var res = new List<(string?, Frame)>();
        foreach (var key in DrawOrder)
        {
            if (layers.TryGetValue(key, out var fs))
                res.AddRange(fs.Select(f => ((string?) key, f)));
            if (layers.TryGetValue("marking:" + key, out var ms))
                res.AddRange(ms.Select(f => ((string?) null, f)));
        }

        return res;
    }

    private static Frame? Tinted(Frame? f, string hex) => f == null ? null : RsiLoader.Tint(f, Color.FromHex(hex));
}
