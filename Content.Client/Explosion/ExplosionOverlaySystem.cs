using Content.Shared.Explosion;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System.Collections.Generic;

namespace Content.Client.Explosion;

/// <summary>
///     This system is responsible for showing the client-side explosion effects (light source & fire-overlay). The
///     fire overlay code is just a bastardized version of the atmos plasma fire overlay and uses the same texture.
/// </summary>
public sealed class ExplosionOverlaySystem : EntitySystem
{
    private ExplosionOverlay _overlay = default!;

    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    /// <summary>
    ///     For how many seconds should an explosion stay on-screen once it has finished expanding?
    /// </summary>
    public const float ExplosionPersistence = 0.2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ExplosionEvent>(OnExplosion);
        SubscribeNetworkEvent<ExplosionOverlayUpdateEvent>(HandleExplosionUpdate);

        var overlayManager = IoCManager.Resolve<IOverlayManager>();
        _overlay = new ExplosionOverlay();
        if (!overlayManager.HasOverlay<ExplosionOverlay>())
            overlayManager.AddOverlay(_overlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // increment the lifetime of completed explosions, and remove them if they have been ons screen for more
        // than ExplosionPersistence seconds
        foreach (var explosion in _overlay.CompletedExplosions.ToArray())
        {
            explosion.Lifetime += frameTime;

            if (explosion.Lifetime >= ExplosionPersistence)
            {
                EntityManager.QueueDeleteEntity(explosion.LightEntity);
                _overlay.CompletedExplosions.Remove(explosion);
            }
        }
    }

    /// <summary>
    ///     The server has processed some explosion. This updates the client-side overlay so that the area covered
    ///     by the fire-visual matches up with the area that the explosion has affected.
    /// </summary>
    private void HandleExplosionUpdate(ExplosionOverlayUpdateEvent args)
    {
        if (_overlay.ActiveExplosion == null)
            return;

        _overlay.Index = args.Index;
        if (_overlay.Index <= _overlay.ActiveExplosion.Tiles.Count)
            return;

        // the explosion has finished expanding
        _overlay.Index = 0;
        _overlay.CompletedExplosions.Add(_overlay.ActiveExplosion);
        _overlay.ActiveExplosion = null;
    }

    /// <summary>
    ///     A new explosion occurred. This prepares the client-side light entity and stores the
    ///     explosion/fire-effect overlay data.
    /// </summary>
    private void OnExplosion(ExplosionEvent args)
    {
        if (!_protoMan.TryIndex(args.TypeID, out ExplosionPrototype? type))
            return;

        // spawn in a light source at the epicenter
        var lightEntity = EntityManager.SpawnEntity("ExplosionLight", args.Epicenter);
        var light = EnsureComp<PointLightComponent>(lightEntity);
        light.Energy = light.Radius = args.Tiles.Count;
        light.Color = type.LightColor;

        if (_overlay.ActiveExplosion != null)
            _overlay.CompletedExplosions.Add(_overlay.ActiveExplosion);

        _overlay.ActiveExplosion = new(args, type, lightEntity);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        var overlayManager = IoCManager.Resolve<IOverlayManager>();
        if (overlayManager.HasOverlay<ExplosionOverlay>())
            overlayManager.RemoveOverlay<ExplosionOverlay>();
    }
}

internal class Explosion
{
    public Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> Tiles;
    public List<float> Intensity;
    public EntityUid LightEntity;
    public MapId Map;

    public Matrix3 SpaceMatrix;

    /// <summary>
    ///     How long have we been drawing this explosion, starting from the time the explosion was fully drawn.
    /// </summary>
    public float Lifetime;

    /// <summary>
    ///     The textures used for the explosion fire effect. Each fire-state is associated with an explosion
    ///     intensity range, and each stat itself has several textures.
    /// </summary>
    public List<Texture[]> FireFrames = new();

    public Color? FireColor;

    internal Explosion(ExplosionEvent args, ExplosionPrototype type, EntityUid lightEntity)
    {
        SpaceMatrix = args.SpaceMatrix;
        Map = args.Epicenter.MapId;
        Tiles = args.Tiles;
        Intensity = args.Intensity;
        FireColor = type.FireColor;
        LightEntity = lightEntity;

        var fireRsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(type.TexturePath).RSI;
        foreach (var state in fireRsi)
        {
            FireFrames.Add(state.GetFrames(RSI.State.Direction.South));
            if (FireFrames.Count == type.FireStates)
                break;
        }
    }
}
