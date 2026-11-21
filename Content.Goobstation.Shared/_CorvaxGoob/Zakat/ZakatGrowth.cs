using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// Mutations of an enslaved host («раб улья»). Bought on the host's circular map, in any order.
/// </summary>
[Serializable, NetSerializable]
public enum ZakatMutation : byte
{
    SpaceResistance,
    NoPain,
    Regeneration,
    LivingArmor,
    RotGrowths,
    ParasiteHeart,
    Claws,
    MeatHarpoon,
    Devourer,
}

/// <summary>
/// Buildings of the born disease, placed on its own flesh.
/// </summary>
[Serializable, NetSerializable]
public enum ZakatStructure : byte
{
    Spewer,
    SmallCocoon,
    HunterCocoon,
    LargeCocoon,
    Wall,
    Door,
    SporeSpewer,
}

/// <summary>
/// The disease's own growth («Рост») and the slave mutation catalogue.
/// Growth levels are 1-based; level N costs <see cref="GrowthCosts"/>[N - 1].
/// </summary>
public static class ZakatGrowth
{
    public static readonly float[] GrowthCosts = { 25f, 50f, 75f, 1000f };

    public static int MaxGrowth => GrowthCosts.Length;

    /// <summary>Рост 1: taking control of a host.</summary>
    public const int PossessLevel = 1;

    /// <summary>Рост 2: «разум улья», enslaving hosts and their free mutation order.</summary>
    public const int HiveMindLevel = 2;

    public const float EnslaveCost = 20f;

    /// <summary>Рост 3: «рождение», the disease comes into the world as a core of flesh.</summary>
    public const int BirthLevel = 3;

    /// <summary>Рост 4: the predatory vines burst out of the core. Opens only with this much captured flesh.</summary>
    public const int VineLevel = 4;
    public const int VineTiles = 200;

    /// <summary>
    /// Living infected hosts (the flesh's creatures don't count) needed before «Рост» 2 and 3 open.
    /// </summary>
    public static int RequiredHosts(int level) => level switch
    {
        2 => 5,
        3 => 10,
        _ => 0,
    };

    /// <summary>Capturing one floor tile with flesh. (Walls aren't covered any more since upd1, they are broken.)</summary>
    public const float CaptureCost = 1f;
    public const float WallCaptureCost = 2f;

    /// <summary>upd1: one blow of the flesh at a wall, window or airlock next to it (like the blob's attack).</summary>
    public const float AttackCost = 2f;
    public const float AttackStructural = 100f;
    public const float AttackBlunt = 10f;
    public static readonly TimeSpan AttackCooldown = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan CaptureCooldown = TimeSpan.FromSeconds(0.25);

    /// <summary>«освободить от оков»: biomass for the burst host, the flesh it leaves is 3x2.</summary>
    public const float LiberateBiomass = 20f;
    public const int LiberateWidth = 3;
    public const int LiberateHeight = 2;

    /// <summary>«создать преторианца».</summary>
    public const float PraetorianCost = 200f;

    public static readonly ZakatStructure[] Structures =
    {
        ZakatStructure.Spewer,
        ZakatStructure.SmallCocoon,
        ZakatStructure.HunterCocoon,
        ZakatStructure.LargeCocoon,
        ZakatStructure.Wall,
        ZakatStructure.Door,
        ZakatStructure.SporeSpewer,
    };

    public static float Cost(ZakatStructure structure) => structure switch
    {
        ZakatStructure.Spewer => 25f,
        ZakatStructure.SmallCocoon => 30f,
        ZakatStructure.HunterCocoon => 50f,
        ZakatStructure.LargeCocoon => 40f,
        ZakatStructure.Wall => 5f,
        ZakatStructure.Door => 5f,
        ZakatStructure.SporeSpewer => 35f,
        _ => 30f,
    };

    public static string Prototype(ZakatStructure structure) => structure switch
    {
        ZakatStructure.Spewer => "ZakatSpewer",
        ZakatStructure.SmallCocoon => "ZakatCocoonSmall",
        ZakatStructure.HunterCocoon => "ZakatCocoonHunter",
        ZakatStructure.LargeCocoon => "ZakatCocoonLarge",
        ZakatStructure.Wall => "ZakatBiomassWall",
        ZakatStructure.Door => "ZakatBiomassDoor",
        ZakatStructure.SporeSpewer => "ZakatSporeSpewer",
        _ => "ZakatSpewer",
    };

    public static readonly ZakatMutation[] Mutations =
    {
        ZakatMutation.SpaceResistance,
        ZakatMutation.NoPain,
        ZakatMutation.Regeneration,
        ZakatMutation.LivingArmor,
        ZakatMutation.RotGrowths,
        ZakatMutation.ParasiteHeart,
        ZakatMutation.Claws,
        ZakatMutation.MeatHarpoon,
        ZakatMutation.Devourer,
    };

    public static float Cost(ZakatMutation mutation) => mutation switch
    {
        ZakatMutation.SpaceResistance => 15f,
        ZakatMutation.NoPain => 20f,
        ZakatMutation.Regeneration => 20f,
        ZakatMutation.LivingArmor => 20f,
        ZakatMutation.RotGrowths => 20f,
        ZakatMutation.ParasiteHeart => 25f,
        ZakatMutation.Claws => 10f,
        ZakatMutation.MeatHarpoon => 20f,
        ZakatMutation.Devourer => 20f,
        _ => 20f,
    };

    public static string LocId(ZakatMutation mutation) => $"zakat-mutation-{mutation.ToString().ToLowerInvariant()}";
}
