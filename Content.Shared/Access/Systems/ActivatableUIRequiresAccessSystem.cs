using Content.Shared.Access.Components;
using Content.Shared.Administration;
using Content.Shared.Administration.Managers;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Shared.Access.Systems;
public sealed partial class ActivatableUIRequiresAccessSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ISharedAdminManager _admin = default!; // CMU14: admin bypass

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActivatableUIRequiresAccessComponent, ActivatableUIOpenAttemptEvent>(OnUIOpenAttempt);
        SubscribeLocalEvent<ActivatableUIRequiresAccessComponent, BoundUserInterfaceMessageAttempt>(OnUIMessageAttempt);
    }

    private void OnUIOpenAttempt(Entity<ActivatableUIRequiresAccessComponent> activatableUI, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || _admin.HasAdminFlag(args.User, AdminFlags.Admin))
            return;

        var requiredIdAccess = activatableUI.Comp.RequiredIdAccess;
        if ((requiredIdAccess is not { } required || HasIdAccess(args.User, required)) &&
            _access.IsAllowed(args.User, activatableUI))
            return;

        args.Cancel();
        if (activatableUI.Comp.PopupMessage != null && !args.Silent)
            _popup.PopupEntity(Loc.GetString(activatableUI.Comp.PopupMessage), activatableUI, args.User);
    }

    private void OnUIMessageAttempt(Entity<ActivatableUIRequiresAccessComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (ent.Comp.RequiredIdAccess is { } required &&
            !_admin.HasAdminFlag(args.Actor, AdminFlags.Admin) &&
            !HasIdAccess(args.Actor, required))
            args.Cancel();
    }

    private bool HasIdAccess(EntityUid user, ProtoId<AccessLevelPrototype> required)
    {
        if (!_access.FindAccessItemsInventory(user, out var items))
            return false;

        foreach (var item in items)
        {
            if (_idCard.TryGetIdCard(item, out var card) &&
                TryComp<AccessComponent>(card.Owner, out var access) && access.Enabled && access.Tags.Contains(required))
                return true;
        }

        return false;
    }
}

