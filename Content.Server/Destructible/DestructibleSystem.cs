using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared.Acts;
using Content.Shared.Damage;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Random;
using System;

namespace Content.Server.Destructible
{
    [UsedImplicitly]
    public class DestructibleSystem : EntitySystem
    {
        [Dependency] public readonly IRobustRandom Random = default!;
        [Dependency] public readonly AudioSystem AudioSystem = default!;
        [Dependency] public readonly ActSystem ActSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<DestructibleComponent, DamageChangedEvent>(Execute);
        }

        /// <summary>
        ///     Check if any thresholds were reached. if they were, execute them.
        /// </summary>
        public void Execute(EntityUid uid, DestructibleComponent component, DamageChangedEvent args)
        {
            foreach (var threshold in component.Thresholds)
            {
                if (threshold.Reached(args.Damageable, this))
                {
                    RaiseLocalEvent(uid, new DamageThresholdReached(component, threshold));

                    threshold.Execute(component.Owner, this);
                }
            }
        }

        // FFS this shouldn't be this hard. Maybe this should just be a field of the destructible component.....
        /// <summary>
        ///     Figure out how much damage an entity needs to have in order to be destroyed.
        /// </summary>
        /// <remarks>
        ///     This assumes that this entity has some sort of destruction or breakage behavior triggered by a
        ///     total-damage threshold.
        /// </remarks>
        /// <returns></returns>
        public float DestroyedAt(EntityUid uid, DestructibleComponent? destructible = null)
        {
            if (!Resolve(uid, ref destructible))
                return float.NaN;

            // We have nested for loops here, but the vast majority of components only have one threshold with 1-3 behaviors.
            // Really, this should JUST be a property of the damageable component.
            var damageNeeded = float.MaxValue;
            foreach (var threshold in destructible.Thresholds)
            {
                if (threshold.Trigger is not DamageTrigger trigger)
                    continue;

                foreach (var behavior in threshold.Behaviors)
                {
                    if (behavior is DoActsBehavior actBehavior &&
                        actBehavior.HasAct(ThresholdActs.Destruction | ThresholdActs.Breakage))
                    {
                        damageNeeded = Math.Min(damageNeeded, trigger.Damage);
                    }
                }
            }
            return damageNeeded;
        }
    }

    // Currently only used for destructible integration tests. Unless other uses are found for this, maybe this should just be removed and the tests redone.
    /// <summary>
    ///     Event raised when a <see cref="DamageThreshold"/> is reached.
    /// </summary>
    public class DamageThresholdReached : EntityEventArgs
    {
        public readonly DestructibleComponent Parent;

        public readonly DamageThreshold Threshold;

        public DamageThresholdReached(DestructibleComponent parent, DamageThreshold threshold)
        {
            Parent = parent;
            Threshold = threshold;
        }
    }
}
