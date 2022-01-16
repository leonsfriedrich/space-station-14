using System;
using Content.Server.Atmos.Piping.Unary.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [Serializable]
    [DataDefinition]
    public class DumpCanisterBehavior : IThresholdBehavior
    {
        public bool Execute(EntityUid owner, DestructibleSystem system)
        {
            system.EntityManager.EntitySysManager.GetEntitySystem<GasCanisterSystem>().PurgeContents(owner);
            return true;
        }
    }
}
