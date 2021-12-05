using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion;

[UsedImplicitly]
public sealed class ExplosionOverlay : Overlay
{
    /// <summary>
    ///     The explosion that needs to be drawn. This explosion is currently being processed by the server and
    ///     expanding outwards.
    /// </summary>
    internal Explosion? ActiveExplosion;

    /// <summary>
    ///     This index specifies what parts of the currently expanding explosion should be drawn.
    /// </summary>
    public int Index;

    /// <summary>
    ///     These explosions have finished expanding, but we will draw for a few more frames. This is important for
    ///     small explosions, as otherwise they disappear far too quickly.
    /// </summary>
    internal List<Explosion> CompletedExplosions = new ();

    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    /// <summary>
    ///     How intense does the explosion have to be at a tile to advance to the next fire texture state?
    /// </summary>
    public const int IntensityPerState = 12;

    public ExplosionOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var drawHandle = args.WorldHandle;
        var worldBounds = _eyeManager.GetWorldViewbounds();

        if (ActiveExplosion != null)
        {
            var gridBounds = ActiveExplosion.Grid.InvWorldMatrix.TransformBox(worldBounds);
            DrawExplosion(drawHandle, gridBounds, ActiveExplosion, Index);
        }

        foreach (var exp in CompletedExplosions)
        {
            var gridBounds = exp.Grid.InvWorldMatrix.TransformBox(worldBounds);
            DrawExplosion(drawHandle, gridBounds, exp, exp.Intensity.Count);
        }

        drawHandle.SetTransform(Matrix3.Identity);
    }

    private void DrawExplosion(DrawingHandleWorld drawHandle, Box2 gridBounds, Explosion exp, int index)
    {
        if (exp.Grid.ParentMapId != _eyeManager.CurrentMap)
            return;

        drawHandle.SetTransform(exp.Grid.WorldMatrix);

        for (var j = 0; j < index; j++)
        {
            if (!exp.Tiles.TryGetValue(j, out var tiles)) continue;

            var frameIndex = (int) Math.Min(exp.Intensity[j] / IntensityPerState, exp.FireFrames.Count - 1);
            var frames = exp.FireFrames[frameIndex];
            DrawExplodingTiles(drawHandle, exp.Grid, tiles, gridBounds, frames, exp.FireColor);
        }
    }

    private void DrawExplodingTiles(DrawingHandleWorld drawHandle, IMapGrid grid, HashSet<Vector2i> tiles, Box2 bounds, Texture[] frames, Color? modulate)
    {
        foreach (var tile in tiles)
        {
            if (!bounds.Contains(grid.GridTileToLocal(tile).Position))
                continue;

            var texture = _robustRandom.Pick(frames);
            drawHandle.DrawTexture(texture, new Vector2(tile.X, tile.Y), modulate);
        }
    }
}
