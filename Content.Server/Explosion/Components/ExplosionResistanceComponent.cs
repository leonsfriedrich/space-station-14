using Content.Shared.Explosion;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;
using Robust.Shared.ViewVariables;
using System.Collections.Generic;

namespace Content.Server.Explosion.Components;

/// <summary>
///     Component that provides entities with explosion resistance.
/// </summary>
/// <remarks>
///     This is desirable over just using damage modifier sets, given that equipment like bomb-suits need to
///     significantly reduce the damage, but shouldn't be silly overpowered in regular combat.
/// </remarks>
[RegisterComponent]
[ComponentProtoName("ExplosionResistance")]
[Friend(typeof(ExplosionSystem))]
public class ExplosionResistanceComponent : Component
{
    /// <summary>
    ///     The resistance values for this component, This fraction is added to the total resistance.
    /// </summary>
    [DataField("resistance")]
    public float GlobalResistance = 0;

    /// <summary>
    ///     Like <see cref="GlobalResistance"/>, but specified specific to each explosion type for more customizability.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("resistances", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, ExplosionPrototype>))]
    public Dictionary<string, float> Resistances = new();
}
