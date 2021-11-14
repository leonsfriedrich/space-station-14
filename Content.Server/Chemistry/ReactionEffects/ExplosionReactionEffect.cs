using System;
using Content.Server.Chemistry.Components.SolutionManager;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Chemistry.ReactionEffects
{
    [DataDefinition]
    public class ExplosionReactionEffect : IReactionEffect
    {
        /// <summary>
        ///     The type of explosion. Determines damage types and tile break chance scaling.
        /// </summary>
        [DataField("explosionType", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<ExplosionPrototype>))]
        public string ExplosionType = default!;

        /// <summary>
        ///     The max intensity the explosion can have at a given tile. Places an upper limit of damage and tile break
        ///     chance.
        /// </summary>
        [DataField("maxIntensity")]
        public float MaxIntensity = 5;

        /// <summary>
        ///     How quickly intensity drops off as you move away from the epicenter
        /// </summary>
        [DataField("intensitySlope")]
        public float IntensitySlope = 1;

<<<<<<< HEAD
        /// <summary>
        ///     The maximum total intensity that this chemical reaction can achieve. Basically here to prevent people
        ///     from creating a nuke by collecting enough potassium and water.
        /// </summary>
        /// <remarks>
        ///     A slope of 1 and MaxTotalIntensity of 100 corresponds to a radius of around 4.5 tiles.
        /// </remarks>
        [DataField("maxTotalIntensity")]
        public float MaxTotalIntensity = 100;
=======
        public void React(Solution solution, EntityUid solutionEntity, double intensity, IEntityManager entityManager)
        {
            var floatIntensity = (float) intensity;

            if (!entityManager.HasComponent<SolutionContainerManagerComponent>(solutionEntity))
                return;
>>>>>>> master

        /// <summary>
        ///     The intensity of the explosion per unit reaction.
        /// </summary>
        [DataField("intensityPerUnit")]
        public float IntensityPerUnit = 1;

        public void React(Solution solution, IEntity solutionEntity, double quantity)
        {
            var intensity = (float) Math.Min(quantity * IntensityPerUnit, MaxTotalIntensity);

<<<<<<< HEAD
            EntitySystem.Get<ExplosionSystem>().QueueExplosion(
                solutionEntity.Uid,
                ExplosionType,
                intensity,
                IntensitySlope,
                MaxIntensity);
=======
            //Calculate intensities
            var finalDevastationRange = (int)MathF.Round(_devastationRange * floatIntensity);
            var finalHeavyImpactRange = (int)MathF.Round(_heavyImpactRange * floatIntensity);
            var finalLightImpactRange = (int)MathF.Round(_lightImpactRange * floatIntensity);
            var finalFlashRange = (int)MathF.Round(_flashRange * floatIntensity);
            EntitySystem.Get<ExplosionSystem>().SpawnExplosion(solutionEntity, finalDevastationRange,
                finalHeavyImpactRange, finalLightImpactRange, finalFlashRange);
>>>>>>> master
        }
    }
}
