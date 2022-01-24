using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Explosion.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Robust.Server.Containers;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityLookup _entityLookup = default!;

    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly NodeGroupSystem _nodeGroupSystem = default!;
    [Dependency] private readonly CameraRecoilSystem _recoilSystem = default!;


    // TODO EXPLOSION MAKE THESE CVARS

    public const ushort DefaultTileSize = 5;

    #region cvars
    public int MaxIterations { get; private set; }
    public int MaxArea { get; private set; }
    public float MaxProcessingTime { get; private set; }
    public int TilesPerTick { get; private set; }
    public bool EnablePhysicsThrow { get; private set; }
    public bool SleepNodeSys { get; private set; }
    public bool IncrementalTileBreaking { get; private set; }
    #endregion

    private AudioParams _audioParams = AudioParams.Default.WithVolume(-3f);

    public override void Initialize()
    {
        base.Initialize();

        // handled in ExplosionSystemGridMap.cs
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<GridStartupEvent>(OnGridStartup);
        SubscribeLocalEvent<ExplosionResistanceComponent, GetExplosionResistanceEvent>(OnGetResistance);
        _mapManager.TileChanged += MapManagerOnTileChanged;

        // handled in ExplosionSystemAirtight.cs
        SubscribeLocalEvent<AirtightComponent, DamageChangedEvent>(OnAirtightDamaged);

        _cfg.OnValueChanged(CCVars.ExplosionTilesPerTick, value => TilesPerTick = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionPhysicsThrow, value => EnablePhysicsThrow = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionSleepNodeSys, value => SleepNodeSys = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionMaxArea, value => MaxArea = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionMaxIterations, value => MaxIterations = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionMaxProcessingTime, value => MaxProcessingTime = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionIncrementalTileBreaking, value => IncrementalTileBreaking = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _mapManager.TileChanged -= MapManagerOnTileChanged;
    }

    private void OnGetResistance(EntityUid uid, ExplosionResistanceComponent component, GetExplosionResistanceEvent args)
    {
        args.Resistance += component.GlobalResistance;
        if (component.Resistances.TryGetValue(args.ExplotionPrototype, out var resistance))
            args.Resistance += resistance;
    }

    /// <summary>
    ///     Given an entity with an explosive component, spawn the appropriate explosion.
    /// </summary>
    /// <remarks>
    ///     Also accepts radius or intensity arguments. This is useful for explosives where the intensity is not
    ///     specified in the yaml / by the component, but determined dynamically (e.g., by the quantity of a
    ///     solution in a reaction).
    /// </remarks>
    public void TriggerExplosive(EntityUid uid, ExplosiveComponent? explosive = null, bool delete = true, float? totalIntensity = null, float? radius = null)
    {
        // log missing: false, because some entities (e.g. liquid tanks) attempt to trigger explosions when damaged,
        // but may not actually be explosive.
        if (!Resolve(uid, ref explosive, logMissing: false))
            return;

        // No reusable explosions here.
        if (explosive.Exploded)
            return;

        explosive.Exploded = true;

        // Override the explosion intensity if optional arguments were provided.
        if (radius != null)
            totalIntensity ??= RadiusToIntensity((float) radius, explosive.IntensitySlope, explosive.MaxIntensity);
        totalIntensity ??= explosive.TotalIntensity;

        QueueExplosion(uid,
            explosive.ExplosionType,
            (float) totalIntensity,
            explosive.IntensitySlope,
            explosive.MaxIntensity);

        if (delete)
            EntityManager.QueueDeleteEntity(uid);
    }

    /// <summary>
    ///     Find the strength needed to generate an explosion of a given radius. More useful for radii larger then 4, when the explosion becomes less "blocky".
    /// </summary>
    /// <remarks>
    ///     This assumes the explosion is in a vacuum / unobstructed. Given that explosions are not perfectly
    ///     circular, here radius actually means the sqrt(Area/pi), where the area is the total number of tiles
    ///     covered by the explosion. Until you get to radius 30+, this is functionally equivalent to the
    ///     actual radius.
    /// </remarks>
    public float RadiusToIntensity(float radius, float slope, float maxIntensity = 0)
    {
        // If you consider the intensity at each tile in an explosion to be a height. Then a circular explosion is
        // shaped like a cone. So total intensity is like the volume of a cone with height = slope * radius. Of
        // course, as the explosions are not perfectly circular, this formula isn't perfect, but the formula works
        // reasonably well.

        // TODO EXPLOSION I guess this should actually use the formula for the volume of a distorted octagonal frustum?

        var coneVolume = slope * MathF.PI / 3 * MathF.Pow(radius, 3);

        if (maxIntensity <= 0 || slope * radius < maxIntensity)
            return coneVolume;

        // This explosion is limited by the maxIntensity.
        // Instead of a cone, we have a conical frustum.

        // Subtract the volume of the missing cone segment, with height:
        var h = slope * radius - maxIntensity;
        return coneVolume - h * MathF.PI / 3 * MathF.Pow(h / slope, 2);
    }


    /// <summary>
    ///     Inverse formula for <see cref="RadiusToIntensity"/>
    /// </summary>
    public float IntensityToRadius(float totalIntensity, float slope, float maxIntensity)
    {
        // max radius to avoid being capped by max-intensity
        var r0 = maxIntensity / slope;

        // volume at r0
        var v0 = RadiusToIntensity(r0, slope);

        if (totalIntensity <= v0)
        {
            // maxIntensity is a non-issue, can use simple inverse formula
            return MathF.Cbrt(3 * totalIntensity / (slope * MathF.PI));
        }

        return r0 * (MathF.Sqrt(12 * totalIntensity/ v0 - 3) / 6 + 0.5f);
    }

    /// <summary>
    ///     Queue an explosions, centered on some entity.
    /// </summary>
    public void QueueExplosion(EntityUid uid,
        string typeId,
        float intensity,
        float slope,
        float maxTileIntensity)
    {
        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform))
            return;

        QueueExplosion(transform.MapPosition, typeId, intensity, slope, maxTileIntensity);
    }

    /// <summary>
    ///     Queue an explosion, with a specified epicenter and set of starting tiles.
    /// </summary>
    public void QueueExplosion(MapCoordinates epicenter,
        string typeId,
        float totalIntensity,
        float slope,
        float maxTileIntensity)
    {
        if (totalIntensity <= 0 || slope <= 0)
            return;

        if (!_prototypeManager.TryIndex<ExplosionPrototype>(typeId, out var type))
        {
            Logger.Error($"Attempted to spawn unknown explosion prototype: {type}");
            return;
        }

        _explosionQueue.Enqueue(() => SpawnExplosion(epicenter, type, totalIntensity,
            slope, maxTileIntensity));
    }

    /// <summary>
    ///     This function actually spawns the explosion. It returns an <see cref="Explosion"/> instance with
    ///     information about the affected tiles for the explosion system to process. It will also trigger the
    ///     camera shake and sound effect.
    /// </summary>
    private Explosion? SpawnExplosion(MapCoordinates epicenter,
        ExplosionPrototype type,
        float totalIntensity,
        float slope,
        float maxTileIntensity)
    {
        var results = GetExplosionTiles(epicenter, type.ID, totalIntensity, slope, maxTileIntensity);

        if (results == null)
            return null;

        var (iterationIntensity, spaceData, gridData, spaceMatrix) = results.Value;

        RaiseNetworkEvent(GetExplosionEvent(epicenter, type.ID, spaceMatrix, spaceData, gridData.Values, iterationIntensity));

        // camera shake
        CameraShake(iterationIntensity.Count * 2.5f, epicenter, totalIntensity);

        //For whatever bloody reason, sound system requires ENTITY coordinates.
        var mapEntityCoords = EntityCoordinates.FromMap(EntityManager, _mapManager.GetMapEntityId(epicenter.MapId), epicenter);

        // play sound. 
        var audioRange = iterationIntensity.Count * 5;
        var filter = Filter.Pvs(epicenter).AddInRange(epicenter, audioRange);
        SoundSystem.Play(filter, type.Sound.GetSound(), mapEntityCoords, _audioParams);

        return new Explosion(this,
            type,
            spaceData,
            gridData.Values.ToList(),
            iterationIntensity,
            epicenter,
            spaceMatrix
            );
    }

    /// <summary>
    ///     Constructor for the shared <see cref="ExplosionEvent"/> using the server-exclusive explosion classes.
    /// </summary>
    public ExplosionEvent GetExplosionEvent(MapCoordinates epicenter, string id, Matrix3 spaceMatrix, SpaceExplosion? spaceData, IEnumerable<GridExplosion> gridData, List<float> iterationIntensity)
    {
        var spaceTiles = spaceData?.TileSets;

        Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> tileSets = new();
        foreach (var grid in gridData)
        {
            tileSets.Add(grid.GridId, grid.TileSets);
        }

        return new ExplosionEvent(_explosionCounter, epicenter, id, iterationIntensity, spaceTiles, tileSets, spaceMatrix);
    }

    private void CameraShake(float range, MapCoordinates epicenter, float totalIntensity)
    {
        var players = Filter.Empty();
        players.AddInRange(epicenter, range, _playerManager, EntityManager);

        foreach (var player in players.Recipients)
        {
            if (player.AttachedEntity is not EntityUid uid)
                continue;

            var playerPos = Transform(player.AttachedEntity!.Value).WorldPosition;
            var delta = epicenter.Position - playerPos;

            if (delta.EqualsApprox(Vector2.Zero))
                delta = new(0.01f, 0);

            var distance = delta.Length;
            var effect = 5 * MathF.Pow(totalIntensity, 0.5f) * (1 - distance / range);
            if (effect > 0.01f)
                _recoilSystem.KickCamera(uid, -delta.Normalized * effect);
        }
    }
}
