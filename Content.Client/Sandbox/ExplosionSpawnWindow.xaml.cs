using System.Collections.Generic;
using Content.Shared.Explosion;
using JetBrains.Annotations;
using Robust.Client.AutoGenerated;
using Robust.Client.Console;
using Robust.Client.Player;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.OptionButton;

namespace Content.Client.Sandbox
{
    [GenerateTypedNameReferences]
    [UsedImplicitly]
    public partial class ExplosionSpawnWindow : SS14Window
    {
        [Dependency] private readonly IClientConsoleHost _conHost = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        private List<MapId> _mapData = new();
        private List<string> _explosionTypes = new();

        /// <summary>
        ///     Used to prevent uneccesary preview updates when setting fields.
        /// </summary>
        private bool _pausePreview ;

        public ExplosionSpawnWindow()
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            ExplosionOption.OnItemSelected += ExplosionSelected ;
            MapOptions.OnItemSelected += MapSelected;
            Recentre.OnPressed += (_) => SetLocation();
            Directed.OnToggled += DirectedToggled;
            Spawn.OnPressed += SubmitButtonOnOnPressed;

            Preview.OnToggled += (_) => UpdatePreview();
            MapX.OnValueChanged += (_) => UpdatePreview();
            MapY.OnValueChanged += (_) => UpdatePreview();
            Intensity.OnValueChanged += (_) => UpdatePreview();
            Slope.OnValueChanged += (_) => UpdatePreview();
            MaxIntensity.OnValueChanged += (_) => UpdatePreview();
            Angle.OnValueChanged += (_) => UpdatePreview();
            Spread.OnValueChanged += (_) => UpdatePreview();
            Distance.OnValueChanged += (_) => UpdatePreview();
        }

        private void ExplosionSelected(ItemSelectedEventArgs args)
        {
            ExplosionOption.SelectId(args.Id);
            UpdatePreview();
        }

        private void MapSelected(ItemSelectedEventArgs args)
        {
            MapOptions.SelectId(args.Id);
            UpdatePreview();
        }

        private void DirectedToggled(ButtonToggledEventArgs args)
        {
            DirectedBox.Visible = Directed.Pressed;

            // Is there really no way to easily get auto-resizing windows!?
            if (DirectedBox.Visible)
                SetHeight = Height + DirectedBox.MaxHeight;
            else
                SetHeight = Height - DirectedBox.MaxHeight;

            UpdatePreview();
        }

        protected override void EnteredTree()
        {
            SetLocation();
            UpdateExplosionTypeOptions();
        }

        private void UpdateExplosionTypeOptions()
        {
            _explosionTypes.Clear();
            ExplosionOption.Clear();
            foreach (var type in _prototypeManager.EnumeratePrototypes<ExplosionPrototype>())
            {
                _explosionTypes.Add(type.ID);
                ExplosionOption.AddItem(type.ID);
            }
        }

        private void UpdateMapOptions()
        {
            _mapData.Clear();
            MapOptions.Clear();
            foreach (var map in _mapManager.GetAllMapIds())
            {
                _mapData.Add(map);
                MapOptions.AddItem(map.ToString());
            }
        }

        /// <summary>
        ///     Set the current grid & indices based on the attached entities current location.
        /// </summary>
        private void SetLocation()
        {
            UpdateMapOptions();

            var transform = _playerManager.LocalPlayer?.ControlledEntity?.Transform;
            if (transform == null)
                return;

            _pausePreview = true;
            MapOptions.Select(_mapData.IndexOf(transform.MapID));
            (MapX.Value,  MapY.Value) = transform.MapPosition.Position;
            _pausePreview = false;

            UpdatePreview();
        }

        private void UpdatePreview(bool clear = false)
        {
            if (_pausePreview)
                return;
        }
        
        private void SubmitButtonOnOnPressed(ButtonEventArgs args)
        {
            Preview.Pressed = false;

            // for the actual explosion, we will just re-use the explosion command.
            // so assemble command arguments:
            var mapId = _mapData[MapOptions.SelectedId];
            var explosionType = _explosionTypes[ExplosionOption.SelectedId];
            var cmd = $"explosion {MapX.Value} {MapY.Value} {Intensity.Value} {mapId} " +
                $"{Slope.Value} {MaxIntensity.Value} {explosionType}";

            if (Directed.Pressed)
                cmd += $" {Angle.Value} {Spread.Value} {Distance.Value}";

            _conHost.ExecuteCommand(cmd);
        }
    }
}
