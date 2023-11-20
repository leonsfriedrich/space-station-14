using Content.Server.Administration.Logs;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Cargo.Systems;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems;

public sealed class GasCanisterSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasCanisterComponent, ComponentStartup>(OnCanisterStartup);
        SubscribeLocalEvent<GasCanisterComponent, AtmosDeviceUpdateEvent>(OnCanisterUpdated);
        SubscribeLocalEvent<GasCanisterComponent, ActivateInWorldEvent>(OnCanisterActivate, after: new[] { typeof(LockSystem) });
        SubscribeLocalEvent<GasCanisterComponent, InteractHandEvent>(OnCanisterInteractHand);
        SubscribeLocalEvent<GasCanisterComponent, InteractUsingEvent>(OnCanisterInteractUsing);
        SubscribeLocalEvent<GasCanisterComponent, EntInsertedIntoContainerMessage>(OnCanisterContainerInserted);
        SubscribeLocalEvent<GasCanisterComponent, EntRemovedFromContainerMessage>(OnCanisterContainerRemoved);
        SubscribeLocalEvent<GasCanisterComponent, PriceCalculationEvent>(CalculateCanisterPrice);
        SubscribeLocalEvent<GasCanisterComponent, GasAnalyzerScanEvent>(OnAnalyzed);
        // Bound UI subscriptions
        SubscribeLocalEvent<GasCanisterComponent, GasCanisterHoldingTankEjectMessage>(OnHoldingTankEjectMessage);
        SubscribeLocalEvent<GasCanisterComponent, GasCanisterChangeReleasePressureMessage>(OnCanisterChangeReleasePressure);
        SubscribeLocalEvent<GasCanisterComponent, GasCanisterChangeReleaseValveMessage>(OnCanisterChangeReleaseValve);
        SubscribeLocalEvent<GasCanisterComponent, LockToggledEvent>(OnLockToggled);
    }

    /// <summary>
    /// Completely dumps the content of the canister into the world.
    /// </summary>
    public void PurgeContents(EntityUid uid, GasCanisterComponent? canister = null, TransformComponent? transform = null)
    {
        if (!Resolve(uid, ref canister, ref transform))
            return;

        var environment = _atmos.GetContainingMixture(uid, false, true);

        if (environment is not null)
            _atmos.Merge(environment, canister.Air);

        _adminLogger.Add(LogType.CanisterPurged, LogImpact.Medium, $"Canister {ToPrettyString(uid):canister} purged its contents of {canister.Air:gas} into the environment.");
        canister.Air.Clear();
    }

    private void OnCanisterStartup(EntityUid uid, GasCanisterComponent comp, ComponentStartup args)
    {
        // Ensure container manager.
        var containerManager = EnsureComp<ContainerManagerComponent>(uid);

        // Ensure container.
        _container.EnsureContainer<ContainerSlot>(uid, comp.ContainerName, containerManager);

        if (TryComp<LockComponent>(uid, out var lockComp))
        {
            _appearance.SetData(uid, GasCanisterVisuals.Locked, lockComp.Locked);
        }
    }

    private void DirtyUI(EntityUid uid,
        GasCanisterComponent? canister = null, NodeContainerComponent? nodeContainer = null,
        ContainerManagerComponent? containerManager = null)
    {
        if (!Resolve(uid, ref canister, ref nodeContainer, ref containerManager))
            return;

        var portStatus = false;
        string? tankLabel = null;
        var tankPressure = 0f;

        if (_nodeContainer.TryGetNode(nodeContainer, canister.PortName, out PipeNode? portNode) && portNode.NodeGroup?.Nodes.Count > 1)
            portStatus = true;

        if (containerManager.TryGetContainer(canister.ContainerName, out var tankContainer)
            && tankContainer.ContainedEntities.Count > 0)
        {
            var tank = tankContainer.ContainedEntities[0];
            var tankComponent = Comp<GasTankComponent>(tank);
            tankLabel = Name(tank);
            tankPressure = tankComponent.Air.Pressure;
        }

        _ui.TrySetUiState(uid, GasCanisterUiKey.Key,
            new GasCanisterBoundUserInterfaceState(Name(uid),
                canister.Air.Pressure, portStatus, tankLabel, tankPressure, canister.ReleasePressure,
                canister.ReleaseValve, canister.MinReleasePressure, canister.MaxReleasePressure));
    }

    private void OnHoldingTankEjectMessage(EntityUid uid, GasCanisterComponent canister, GasCanisterHoldingTankEjectMessage args)
    {
        if (!TryComp<ContainerManagerComponent>(uid, out var containerManager)
            || !containerManager.TryGetContainer(canister.ContainerName, out var container))
            return;

        if (container.ContainedEntities.Count == 0)
            return;

        _adminLogger.Add(LogType.CanisterTankEjected, LogImpact.Medium, $"Player {ToPrettyString(args.Session.AttachedEntity.GetValueOrDefault()):player} ejected tank {ToPrettyString(container.ContainedEntities[0]):tank} from {ToPrettyString(uid):canister}");
        container.Remove(container.ContainedEntities[0]);
    }

    private void OnCanisterChangeReleasePressure(EntityUid uid, GasCanisterComponent canister, GasCanisterChangeReleasePressureMessage args)
    {
        var pressure = Math.Clamp(args.Pressure, canister.MinReleasePressure, canister.MaxReleasePressure);

        _adminLogger.Add(LogType.CanisterPressure, LogImpact.Medium, $"{ToPrettyString(args.Session.AttachedEntity.GetValueOrDefault()):player} set the release pressure on {ToPrettyString(uid):canister} to {args.Pressure}");

        canister.ReleasePressure = pressure;
        DirtyUI(uid, canister);
    }

    private void OnCanisterChangeReleaseValve(EntityUid uid, GasCanisterComponent canister, GasCanisterChangeReleaseValveMessage args)
    {
        var impact = LogImpact.High;
        if (TryComp<ContainerManagerComponent>(uid, out var containerManager)
            && containerManager.TryGetContainer(canister.ContainerName, out var container))
        {
            // filling a jetpack with plasma is less important than filling a room with it
            impact = container.ContainedEntities.Count != 0 ? LogImpact.Medium : LogImpact.High;
        }

        var containedGasDict = new Dictionary<Gas, float>();
        var containedGasArray = Gas.GetValues(typeof(Gas));

        for (int i = 0; i < containedGasArray.Length; i++)
        {
            containedGasDict.Add((Gas)i, canister.Air[i]);
        }

        _adminLogger.Add(LogType.CanisterValve, impact, $"{ToPrettyString(args.Session.AttachedEntity.GetValueOrDefault()):player} set the valve on {ToPrettyString(uid):canister} to {args.Valve:valveState} while it contained [{string.Join(", ", containedGasDict)}]");

        canister.ReleaseValve = args.Valve;
        DirtyUI(uid, canister);
    }

    private void OnCanisterUpdated(EntityUid uid, GasCanisterComponent canister, AtmosDeviceUpdateEvent args)
    {
        _atmos.React(canister.Air, canister);

        if (!TryComp<NodeContainerComponent>(uid, out var nodeContainer)
            || !TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        if (!_nodeContainer.TryGetNode(nodeContainer, canister.PortName, out PortablePipeNode? portNode))
            return;

        if (portNode.NodeGroup is PipeNet {NodeCount: > 1} net)
        {
            MixContainerWithPipeNet(canister.Air, net.Air);
        }

        ContainerManagerComponent? containerManager = null;

        // Release valve is open, release gas.
        if (canister.ReleaseValve)
        {
            if (!TryComp(uid, out containerManager)
                || !containerManager.TryGetContainer(canister.ContainerName, out var container))
                return;

            if (container.ContainedEntities.Count > 0)
            {
                var gasTank = Comp<GasTankComponent>(container.ContainedEntities[0]);
                _atmos.ReleaseGasTo(canister.Air, gasTank.Air, canister.ReleasePressure);
            }
            else
            {
                var environment = _atmos.GetContainingMixture(uid, false, true);
                _atmos.ReleaseGasTo(canister.Air, environment, canister.ReleasePressure);
            }
        }

        // If last pressure is very close to the current pressure, do nothing.
        if (MathHelper.CloseToPercent(canister.Air.Pressure, canister.LastPressure))
            return;

        DirtyUI(uid, canister, nodeContainer, containerManager);

        canister.LastPressure = canister.Air.Pressure;

        if (canister.Air.Pressure < 10)
        {
            _appearance.SetData(uid, GasCanisterVisuals.PressureState, 0, appearance);
        }
        else if (canister.Air.Pressure < Atmospherics.OneAtmosphere)
        {
            _appearance.SetData(uid, GasCanisterVisuals.PressureState, 1, appearance);
        }
        else if (canister.Air.Pressure < (15 * Atmospherics.OneAtmosphere))
        {
            _appearance.SetData(uid, GasCanisterVisuals.PressureState, 2, appearance);
        }
        else
        {
            _appearance.SetData(uid, GasCanisterVisuals.PressureState, 3, appearance);
        }
    }

    private void OnCanisterActivate(EntityUid uid, GasCanisterComponent component, ActivateInWorldEvent args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (CheckLocked(uid, component, args.User))
            return;

        // Needs to be here so the locked check still happens if the canister
        // is locked and you don't have permissions
        if (args.Handled)
            return;

        _ui.TryOpen(uid, GasCanisterUiKey.Key, actor.PlayerSession);
        args.Handled = true;
    }

    private void OnCanisterInteractHand(EntityUid uid, GasCanisterComponent component, InteractHandEvent args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (CheckLocked(uid, component, args.User))
            return;

        _ui.TryOpen(uid, GasCanisterUiKey.Key, actor.PlayerSession);
        args.Handled = true;
    }

    private void OnCanisterInteractUsing(EntityUid uid, GasCanisterComponent component, InteractUsingEvent args)
    {
        var container = _container.EnsureContainer<ContainerSlot>(uid, component.ContainerName);

        // Container full.
        if (container.ContainedEntity != null)
            return;

        // Check the used item is valid...
        if (!TryComp<GasTankComponent>(args.Used, out var gasTank) || gasTank.IsValveOpen)
            return;

        // Preventing inserting a tank since if its locked you cant remove it.
        if (CheckLocked(uid, component, args.User))
            return;

        if (!_hands.TryDropIntoContainer(args.User, args.Used, container))
            return;

        _adminLogger.Add(LogType.CanisterTankInserted, LogImpact.Medium, $"Player {ToPrettyString(args.User):player} inserted tank {ToPrettyString(container.ContainedEntities[0]):tank} into {ToPrettyString(uid):canister}");

        args.Handled = true;
    }

    private void OnCanisterContainerInserted(EntityUid uid, GasCanisterComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != component.ContainerName)
            return;

        DirtyUI(uid, component);

        _appearance.SetData(uid, GasCanisterVisuals.TankInserted, true);
    }

    private void OnCanisterContainerRemoved(EntityUid uid, GasCanisterComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != component.ContainerName)
            return;

        DirtyUI(uid, component);

        _appearance.SetData(uid, GasCanisterVisuals.TankInserted, false);
    }

    /// <summary>
    /// Mix air from a gas container into a pipe net.
    /// Useful for anything that uses connector ports.
    /// </summary>
    public void MixContainerWithPipeNet(GasMixture containerAir, GasMixture pipeNetAir)
    {
        var buffer = new GasMixture(pipeNetAir.Volume + containerAir.Volume);

        _atmos.Merge(buffer, pipeNetAir);
        _atmos.Merge(buffer, containerAir);

        pipeNetAir.Clear();
        _atmos.Merge(pipeNetAir, buffer);
        pipeNetAir.Multiply(pipeNetAir.Volume / buffer.Volume);

        containerAir.Clear();
        _atmos.Merge(containerAir, buffer);
        containerAir.Multiply(containerAir.Volume / buffer.Volume);
    }

    private void CalculateCanisterPrice(EntityUid uid, GasCanisterComponent component, ref PriceCalculationEvent args)
    {
        args.Price += _atmos.GetPrice(component.Air);
    }

    /// <summary>
    /// Returns the gas mixture for the gas analyzer
    /// </summary>
    private void OnAnalyzed(EntityUid uid, GasCanisterComponent component, GasAnalyzerScanEvent args)
    {
        args.GasMixtures = new Dictionary<string, GasMixture?> { {Name(uid), component.Air} };
    }

    private void OnLockToggled(EntityUid uid, GasCanisterComponent component, ref LockToggledEvent args)
    {
        _appearance.SetData(uid, GasCanisterVisuals.Locked, args.Locked);
    }

    /// <summary>
    /// Check if the canister is locked, playing its sound and popup if so.
    /// </summary>
    /// <returns>
    /// True if locked, false otherwise.
    /// </returns>
    private bool CheckLocked(EntityUid uid, GasCanisterComponent comp, EntityUid user)
    {
        if (TryComp<LockComponent>(uid, out var lockComp) && lockComp.Locked)
        {
            _popup.PopupEntity(Loc.GetString("gas-canister-popup-denied"), uid, user);
            if (comp.AccessDeniedSound != null)
            {
                _audio.PlayPvs(comp.AccessDeniedSound, uid);
            }

            return true;
        }

        return false;
    }
}
