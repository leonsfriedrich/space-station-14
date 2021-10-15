using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionDebugOverlay : Overlay
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public List<HashSet<Vector2i>>? Tiles;
        public List<float>? Intensity;
        public IMapGrid? Grid;
        public float TotalIntensity;
        public float Damage;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

        private readonly Font _font;
        private readonly Font _smallFont;

        public ExplosionDebugOverlay()
        {
            IoCManager.InjectDependencies(this);

            var cache = IoCManager.Resolve<IResourceCache>();
            _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 16);
            _smallFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {

            if (Tiles == null || Grid == null)
                return;

            if (Tiles.Count < 2 || Tiles[1].Count != 1)
                return;

            switch (args.Space)
            {
                case OverlaySpace.ScreenSpace:
                    DrawScreen(args);
                    break;
                case OverlaySpace.WorldSpace:
                    DrawWorld(args);
                    break;
            }
        }

        private void DrawScreen(OverlayDrawArgs args)
        {
            var handle = args.ScreenHandle;
            var gridXform = _entityManager.GetComponent<ITransformComponent>(Grid!.GridEntityId);

            for (int i = 0; i < Tiles!.Count; i++)
            {
                foreach (var tile in Tiles[i])
                {
                    var worldCenter = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
                    var screenCenter = _eyeManager.WorldToScreen(worldCenter);
                    
                    if (Intensity![i] > 9)
                        screenCenter += (-26, -16);
                    else
                        screenCenter += (-18, -16);

                    handle.DrawString(_font, screenCenter, Intensity![i].ToString("F2"));
                }
            }

            foreach (var epicenter in Tiles[1])
            {
                var gridBox = Box2.UnitCentered.Translated((Vector2) epicenter + 0.5f);

                var worldTopLeft = gridXform.WorldMatrix.Transform(gridBox.TopLeft);
                var worldBottomLeft = gridXform.WorldMatrix.Transform(gridBox.BottomLeft);

                var screenTopLeft = _eyeManager.WorldToScreen(worldTopLeft);
                var screenBottomLeft = _eyeManager.WorldToScreen(worldBottomLeft) + (0, -24);

                handle.DrawString(_smallFont, screenTopLeft, TotalIntensity.ToString(), Color.Black);
                handle.DrawString(_smallFont, screenBottomLeft, Damage.ToString(), Color.Black);
            }
        }

        private void DrawWorld(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;
            var gridXform = _entityManager.GetComponent<ITransformComponent>(Grid!.GridEntityId);

            for (int i = 0; i < Tiles!.Count; i++)
            {
                var color = ColorMap(Intensity![i]);
                var colorTransparent = color;
                colorTransparent.A = 0.4f;

                foreach (var tile in Tiles[i])
                {
                    var centre = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
                    var box = Box2.UnitCentered.Translated(centre);
                    var rotatedBox = new Box2Rotated(box, gridXform.WorldRotation, centre);                    

                    handle.DrawRect(rotatedBox, color, false);
                    handle.DrawRect(rotatedBox, colorTransparent);
                }
            }
        }

        private Color ColorMap(float strength)
        {
            var interp = 1- strength / Intensity![1];
            Color result;
            if (interp < 0.5f)
            {
                result = Color.InterpolateBetween(Color.Red, Color.Orange, interp * 2);
            }
            else
            {
                result = Color.InterpolateBetween(Color.Orange, Color.Yellow, (interp - 0.5f) * 2);
            }
            return result;
        }
    }
}
