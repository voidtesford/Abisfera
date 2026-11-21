using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._CorvaxGoob.MartialArts.Events;

// Abisfera - «Искусство панча»

/// <summary>«Серия панчей»: Harm, Harm, Harm.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class ArtOfThePunchSeriesPerformedEvent : EntityEventArgs;

/// <summary>«Проверка пресса»: Disarm, Harm, Harm.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class ArtOfThePunchAbsCheckPerformedEvent : EntityEventArgs;

/// <summary>«Долой зубы»: Harm, Harm, «Тяжёлая атака» in the head.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class ArtOfThePunchTeethPerformedEvent : EntityEventArgs;
