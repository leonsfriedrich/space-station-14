using Content.Shared.Actions;
using Content.Shared.Actions.ActionTypes;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Strip;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.EntitySystems;

public sealed class ToggleableClothingSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedStrippableSystem _strippable = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly INetManager _net = default!;

    private Queue<EntityUid> _toInsert = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableClothingComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ToggleableClothingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ToggleableClothingComponent, ToggleClothingEvent>(OnToggleClothing);
        SubscribeLocalEvent<ToggleableClothingComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ToggleableClothingComponent, ComponentRemove>(OnRemoveToggleable);
        SubscribeLocalEvent<ToggleableClothingComponent, GotUnequippedEvent>(OnToggleableUnequip);

        SubscribeLocalEvent<AttachedClothingComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<AttachedClothingComponent, GotUnequippedEvent>(OnAttachedUnequip);
        SubscribeLocalEvent<AttachedClothingComponent, ComponentRemove>(OnRemoveAttached);

        SubscribeLocalEvent<ToggleableClothingComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbsEvent);
        SubscribeLocalEvent<ToggleableClothingComponent, GetVerbsEvent<StrippingVerb>>(OnGetStripVerbsEvent);
        SubscribeLocalEvent<AttachedClothingComponent, GetVerbsEvent<StrippingVerb>>(OnGetAttachedStripVerbsEvent);
        SubscribeLocalEvent<ToggleableClothingComponent, DoAfterEvent<ToggleClothingEvent>>(OnDoAfterComplete);
    }

    private void OnGetVerbsEvent(EntityUid uid, ToggleableClothingComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.ClothingUid == null || component.Container == null)
            return;

        var text = component.VerbText ?? component.ToggleAction?.DisplayName;
        if (text == null)
            return;

        if (!_clothing.InSlotWithFlags(uid, component.RequiredFlags))
            return;

        // only the wearer should have access to this verb.
        DebugTools.Assert(args.User == Transform(uid).ParentUid);

        args.Verbs.Add(new()
        {
            EventTarget = uid,
            ExecutionEventArgs = new ToggleClothingEvent() { Performer = args.User },
            Icon = new SpriteSpecifier.Texture(new ResourcePath("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
            Text = Loc.GetString(text),
        });
    }

    private void OnGetStripVerbsEvent(EntityUid uid, ToggleableClothingComponent component, GetVerbsEvent<StrippingVerb> args)
    {
        if (!args.CanInteract || component.ClothingUid == null || component.Container == null || component.StripDelay == null)
            return;

        var text = component.VerbText ?? component.ToggleAction?.DisplayName;
        if (text == null)
            return;

        if (!_clothing.InSlotWithFlags(uid, component.RequiredFlags))
            return;

        args.Verbs.Add(new()
        {
            Act = () => StartDoAfter(args.User, uid, Transform(uid).ParentUid, component),
            Icon = new SpriteSpecifier.Texture(new ResourcePath("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
            Text = Loc.GetString(text),
        });
    }

    private void StartDoAfter(EntityUid user, EntityUid item, EntityUid wearer, ToggleableClothingComponent component)
    {
        // TODO predict do afters & networked clothing toggle.
        if (_net.IsClient)
            return;

        if (component.DoAfterId != null || component.StripDelay == null)
            return;

        var (time, stealth) = _strippable.GetStripTimeModifiers(user, wearer, (float) component.StripDelay.Value.TotalSeconds);

        if (!stealth)
        {
            var popup = Loc.GetString("strippable-component-alert-owner-interact", ("user", Identity.Entity(user, EntityManager)), ("item", item));
            _popupSystem.PopupEntity(popup, wearer, wearer, PopupType.Large);
        }

        var args = new DoAfterEventArgs(user, time, default, wearer, item)
        {
            DistanceThreshold = 1f,
            BreakOnDamage = true,
            BreakOnStun = true,
            BreakOnTargetMove = true,
            RaiseOnTarget = false,
            RaiseOnUsed = true,
            RaiseOnUser = false,
        };

        var doAfter = _doAfter.DoAfter(args, new ToggleClothingEvent() { Performer = user });
        component.DoAfterId = doAfter.ID;
    }

    private void OnGetAttachedStripVerbsEvent(EntityUid uid, AttachedClothingComponent component, GetVerbsEvent<StrippingVerb> args)
    {
        // redirect to the attached entity.
        OnGetStripVerbsEvent(component.AttachedUid, Comp<ToggleableClothingComponent>(component.AttachedUid), args);
    }

    private void OnDoAfterComplete(EntityUid uid, ToggleableClothingComponent component, DoAfterEvent<ToggleClothingEvent> args)
    {
        DebugTools.Assert(component.DoAfterId == args.Id);

        if (component.DoAfterId != args.Id)
        {
            DebugTools.Assert("Mismatched do after IDs?");
            return;
        }

        component.DoAfterId = null;

        if (args.Cancelled)
            return;

        OnToggleClothing(uid, component, args.AdditionalData);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // process delayed insertions. Avoids doing a container insert during a container removal.
        while (_toInsert.TryDequeue(out var uid))
        {
            if (TryComp(uid, out ToggleableClothingComponent? component) && component.ClothingUid != null)
                component.Container?.Insert(component.ClothingUid.Value);
        }
    }

    private void OnInteractHand(EntityUid uid, AttachedClothingComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleCom)
            || toggleCom.Container == null)
            return;

        if (!_inventorySystem.TryUnequip(Transform(uid).ParentUid, toggleCom.Slot, force: true))
            return;

        toggleCom.Container.Insert(uid, EntityManager);
        args.Handled = true;
    }

    /// <summary>
    ///     Called when the suit is unequipped, to ensure that the helmet also gets unequipped.
    /// </summary>
    private void OnToggleableUnequip(EntityUid uid, ToggleableClothingComponent component, GotUnequippedEvent args)
    {
        // If the attached clothing is not currently in the container, this just assumes that it is currently equipped.
        // This should maybe double check that the entity currently in the slot is actually the attached clothing, but
        // if its not, then something else has gone wrong already...
        if (component.Container != null && component.Container.ContainedEntity == null && component.ClothingUid != null)
            _inventorySystem.TryUnequip(args.Equipee, component.Slot, force: true);
    }

    private void OnRemoveToggleable(EntityUid uid, ToggleableClothingComponent component, ComponentRemove args)
    {
        // If the parent/owner component of the attached clothing is being removed (entity getting deleted?) we will
        // delete the attached entity. We do this regardless of whether or not the attached entity is currently
        // "outside" of the container or not. This means that if a hardsuit takes too much damage, the helmet will also
        // automatically be deleted.

        // remove action.
        if (component.ToggleAction?.AttachedEntity != null)
            _actionsSystem.RemoveAction(component.ToggleAction.AttachedEntity.Value, component.ToggleAction);

        if (component.ClothingUid != null)
            QueueDel(component.ClothingUid.Value);
    }

    private void OnRemoveAttached(EntityUid uid, AttachedClothingComponent component, ComponentRemove args)
    {
        // if the attached component is being removed (maybe entity is being deleted?) we will just remove the
        // toggleable clothing component. This means if you had a hard-suit helmet that took too much damage, you would
        // still be left with a suit that was simply missing a helmet. There is currently no way to fix a partially
        // broken suit like this.

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleComp))
            return;

        if (toggleComp.LifeStage > ComponentLifeStage.Running)
            return;

        // remove action.
        if (toggleComp.ToggleAction?.AttachedEntity != null)
            _actionsSystem.RemoveAction(toggleComp.ToggleAction.AttachedEntity.Value, toggleComp.ToggleAction);

        RemComp(component.AttachedUid, toggleComp);
    }

    /// <summary>
    ///     Called if the helmet was unequipped, to ensure that it gets moved into the suit's container.
    /// </summary>
    private void OnAttachedUnequip(EntityUid uid, AttachedClothingComponent component, GotUnequippedEvent args)
    {
        if (component.LifeStage > ComponentLifeStage.Running)
            return;

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleComp))
            return;

        if (toggleComp.LifeStage > ComponentLifeStage.Running)
            return;

        // As unequipped gets called in the middle of container removal, we cannot call a container-insert without causing issues.
        // So we delay it and process it during a system update:
        _toInsert.Enqueue(component.AttachedUid);
    }

    /// <summary>
    ///     Equip or unequip the toggleable clothing.
    /// </summary>
    private void OnToggleClothing(EntityUid uid, ToggleableClothingComponent component, ToggleClothingEvent args)
    {
        if (args.Handled || component.Container == null || component.ClothingUid == null)
            return;

        var parent = Transform(uid).ParentUid;
        if (component.Container.ContainedEntity == null)
            _inventorySystem.TryUnequip(parent, component.Slot);
        else if (_inventorySystem.TryGetSlotEntity(parent, component.Slot, out var existing))
        {
            _popupSystem.PopupEntity(Loc.GetString("toggleable-clothing-remove-first", ("entity", existing)),
                args.Performer, args.Performer);
        }
        else
            _inventorySystem.TryEquip(parent, component.ClothingUid.Value, component.Slot);

        args.Handled = true;
    }

    private void OnGetActions(EntityUid uid, ToggleableClothingComponent component, GetItemActionsEvent args)
    {
        if (component.ClothingUid == null || (args.SlotFlags & component.RequiredFlags) != component.RequiredFlags)
            return;

        if (component.ToggleAction != null)
            args.Actions.Add(component.ToggleAction);
    }

    private void OnInit(EntityUid uid, ToggleableClothingComponent component, ComponentInit args)
    {
        component.Container = _containerSystem.EnsureContainer<ContainerSlot>(uid, component.ContainerId);
    }

    /// <summary>
    ///     On map init, either spawn the appropriate entity into the suit slot, or if it already exists, perform some
    ///     sanity checks. Also updates the action icon to show the toggled-entity.
    /// </summary>
    private void OnMapInit(EntityUid uid, ToggleableClothingComponent component, MapInitEvent args)
    {
        if (component.Container!.ContainedEntity is EntityUid ent)
        {
            DebugTools.Assert(component.ClothingUid == ent, "Unexpected entity present inside of a toggleable clothing container.");
            return;
        }

        if (component.ToggleAction == null
            && _proto.TryIndex(component.ActionId, out InstantActionPrototype? act))
        {
            component.ToggleAction = new(act);
        }

        if (component.ClothingUid != null && component.ToggleAction != null)
        {
            DebugTools.Assert(Exists(component.ClothingUid), "Toggleable clothing is missing expected entity.");
            DebugTools.Assert(TryComp(component.ClothingUid, out AttachedClothingComponent? comp), "Toggleable clothing is missing an attached component");
            DebugTools.Assert(comp?.AttachedUid == uid, "Toggleable clothing uid mismatch");
        }
        else
        {
            var xform = Transform(uid);
            component.ClothingUid = Spawn(component.ClothingPrototype, xform.Coordinates);
            EnsureComp<AttachedClothingComponent>(component.ClothingUid.Value).AttachedUid = uid;
            component.Container.Insert(component.ClothingUid.Value, EntityManager, ownerTransform: xform);
        }

        if (component.ToggleAction != null)
        {
            component.ToggleAction.EntityIcon = component.ClothingUid;
            _actionsSystem.Dirty(component.ToggleAction);
        }
    }
}

public sealed class ToggleClothingEvent : InstantActionEvent { }
