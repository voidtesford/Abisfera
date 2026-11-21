using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// Host evolution tree. Order inside a branch is the unlock order.
/// </summary>
[Serializable, NetSerializable]
public enum ZakatSymptom : byte
{
    Sneeze,
    Cough,
    Pneumonia,
    LungFibrosis,
    PulmonaryEdema,
    Nausea,
    ImmuneSuppression,
    Sweating,
    Dysentery,
    BloodyVomit,
    Rash,
    SkinLesions,
    Psychosis,
    Epilepsy,
    // p2: «органы»
    Tachycardia,
    Hemophilia,
    RotBlood,
    OrganDecay,
}

/// <summary>
/// Periodic actions of symptoms.
/// </summary>
[Serializable, NetSerializable]
public enum ZakatTimer : byte
{
    Sneeze,
    Cough,
    Pneumonia,
    PulmonaryEdema,
    Nausea,
    Dysentery,
    BloodyVomit,
    Rash,
    SkinLesions,
    PsychosisDrop,
    PsychosisStupor,
    PsychosisSelfHit,
    Epilepsy,
    RotBlood,
    OrganDecay,
}

public readonly record struct ZakatSymptomInfo(ZakatSymptom Symptom, int Branch, int Index, float Cost, float Infectivity);

public static class ZakatSymptoms
{
    public static readonly ZakatSymptom[][] Branches =
    {
        new[] { ZakatSymptom.Sneeze, ZakatSymptom.Cough, ZakatSymptom.Pneumonia, ZakatSymptom.LungFibrosis, ZakatSymptom.PulmonaryEdema },
        new[] { ZakatSymptom.Nausea, ZakatSymptom.ImmuneSuppression, ZakatSymptom.Sweating, ZakatSymptom.Dysentery, ZakatSymptom.BloodyVomit },
        new[] { ZakatSymptom.Rash, ZakatSymptom.SkinLesions, ZakatSymptom.Psychosis, ZakatSymptom.Epilepsy },
        new[] { ZakatSymptom.Tachycardia, ZakatSymptom.Hemophilia, ZakatSymptom.RotBlood, ZakatSymptom.OrganDecay },
    };

    /// <summary>
    /// Biomass costs per branch position (the spec numbers).
    /// </summary>
    public static readonly float[] Costs = { 5f, 10f, 15f, 20f, 25f };

    /// <summary>
    /// Host infectivity bonus of symptoms that make the host more contagious.
    /// </summary>
    public static float InfectivityBonus(ZakatSymptom symptom) => symptom switch
    {
        ZakatSymptom.Sneeze => 0.2f,
        ZakatSymptom.Cough => 0.35f,
        ZakatSymptom.Sweating => 0.25f,
        ZakatSymptom.Rash => 0.2f,
        _ => 0f,
    };

    public static ZakatSymptomInfo Get(ZakatSymptom symptom)
    {
        for (var b = 0; b < Branches.Length; b++)
        {
            var idx = Array.IndexOf(Branches[b], symptom);
            if (idx >= 0)
                return new ZakatSymptomInfo(symptom, b, idx, Costs[idx], InfectivityBonus(symptom));
        }

        throw new ArgumentOutOfRangeException(nameof(symptom));
    }

    /// <summary>
    /// The symptom before this one in its branch, if any (it has to be bought first).
    /// </summary>
    public static ZakatSymptom? Previous(ZakatSymptom symptom)
    {
        var info = Get(symptom);
        return info.Index == 0 ? null : Branches[info.Branch][info.Index - 1];
    }

    public static string LocId(ZakatSymptom symptom) => $"zakat-symptom-{symptom.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Symptoms that only hurt the host. They are dropped when the host is enslaved (a slave is a weapon, not a patient).
    /// </summary>
    public static bool IsDebuff(ZakatSymptom symptom) => InfectivityBonus(symptom) <= 0f;

    /// <summary>
    /// How often each periodic action fires.
    /// </summary>
    public static TimeSpan Period(ZakatTimer timer) => TimeSpan.FromSeconds(timer switch
    {
        ZakatTimer.Sneeze => 45,
        ZakatTimer.Cough => 30,
        ZakatTimer.Pneumonia => 60,
        ZakatTimer.PulmonaryEdema => 30,
        ZakatTimer.Nausea => 300,
        ZakatTimer.Dysentery => 60,
        ZakatTimer.BloodyVomit => 60,
        ZakatTimer.Rash => 300,
        ZakatTimer.SkinLesions => 180,
        ZakatTimer.PsychosisDrop => 300,
        ZakatTimer.PsychosisStupor => 600,
        ZakatTimer.PsychosisSelfHit => 60,
        ZakatTimer.Epilepsy => 180,
        ZakatTimer.RotBlood => 60,
        ZakatTimer.OrganDecay => 120,
        _ => 60,
    });

    /// <summary>
    /// Periodic actions a symptom brings.
    /// </summary>
    public static ZakatTimer[] Timers(ZakatSymptom symptom) => symptom switch
    {
        ZakatSymptom.Sneeze => new[] { ZakatTimer.Sneeze },
        ZakatSymptom.Cough => new[] { ZakatTimer.Cough },
        ZakatSymptom.Pneumonia => new[] { ZakatTimer.Pneumonia },
        ZakatSymptom.PulmonaryEdema => new[] { ZakatTimer.PulmonaryEdema },
        ZakatSymptom.Nausea => new[] { ZakatTimer.Nausea },
        ZakatSymptom.Dysentery => new[] { ZakatTimer.Dysentery },
        ZakatSymptom.BloodyVomit => new[] { ZakatTimer.BloodyVomit },
        ZakatSymptom.Rash => new[] { ZakatTimer.Rash },
        ZakatSymptom.SkinLesions => new[] { ZakatTimer.SkinLesions },
        ZakatSymptom.Psychosis => new[] { ZakatTimer.PsychosisDrop, ZakatTimer.PsychosisStupor, ZakatTimer.PsychosisSelfHit },
        ZakatSymptom.Epilepsy => new[] { ZakatTimer.Epilepsy },
        ZakatSymptom.RotBlood => new[] { ZakatTimer.RotBlood },
        ZakatSymptom.OrganDecay => new[] { ZakatTimer.OrganDecay },
        _ => Array.Empty<ZakatTimer>(),
    };
}
