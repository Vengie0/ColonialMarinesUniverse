using Content.Shared.Access.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Access.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(ActivatableUIRequiresAccessSystem))]
public sealed partial class ActivatableUIRequiresAccessComponent : Component
{
    [DataField]
    public LocId? PopupMessage = "lock-comp-has-user-access-fail";

    /// <summary>
    /// When set, non-admin users must present a held or worn ID with this access to use the UI.
    /// </summary>
    [DataField]
    public ProtoId<AccessLevelPrototype>? RequiredIdAccess;
}
