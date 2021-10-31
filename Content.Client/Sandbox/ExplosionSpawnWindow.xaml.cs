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
        private bool _preview;

        public ExplosionSpawnWindow()
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            ExplosionOption.OnItemSelected += ExplosionSelected ;
            MapOptions.OnItemSelected += MapSelected;
            Recentre.OnPressed += (_) => SetLocation();
            Directed.OnToggled += DirectedToggled;
            Preview.OnToggled += PreviewToggled;
            Spawn.OnPressed += SubmitButtonOnOnPressed;

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

        private void PreviewToggled(ButtonToggledEventArgs args)
        {
            _preview = Preview.Pressed;
            if (!_preview)
                _conHost.ExecuteCommand("explosion clear");
            else
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
            UpdateExplosionTypes();
        }

        private void UpdateExplosionTypes()
        {
            _explosionTypes.Clear();
            ExplosionOption.Clear();
            foreach (var type in _prototypeManager.EnumeratePrototypes<ExplosionPrototype>())
            {
                _explosionTypes.Add(type.ID);
                ExplosionOption.AddItem(type.ID);
            }
        }

        private void UpdateMaps()
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
            UpdateMaps();

            var transform = _playerManager.LocalPlayer?.ControlledEntity?.Transform;
            if (transform == null)
                return;

            // avoid a triple preview update when setting values
            _preview = false;

            MapOptions.Select(_mapData.IndexOf(transform.MapID));
            (MapX.Value,  MapY.Value) = transform.MapPosition.Position;

            _preview = Preview.Pressed;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_preview)
                _conHost.ExecuteCommand("explosion preview " + GetCommandArgs());
        }

        /// <summary>
        ///     Parse all the explosion data into arguments for the explosion command.
        /// </summary>
        private string GetCommandArgs()
        {
            var mapId = _mapData[MapOptions.SelectedId];
            var explosionType = _explosionTypes[ExplosionOption.SelectedId];

            var args = $"{MapX.Value} {MapY.Value} {mapId} {Intensity.Value} " +
                $"{Slope.Value} {MaxIntensity.Value} {explosionType}";

            if (Directed.Pressed)
                args += $" {Angle.Value} {Spread.Value} {Distance.Value}";

            return args;
        }

        private void SubmitButtonOnOnPressed(ButtonEventArgs args)
        {
            if (_preview)
            {
                // Clear preview. Need a view to appreciate the fireworks.
                _preview = Preview.Pressed = false;
                _conHost.ExecuteCommand("explosion clear");
            }

            _conHost.ExecuteCommand("explosion spawn " + GetCommandArgs());
        }
    }
}
