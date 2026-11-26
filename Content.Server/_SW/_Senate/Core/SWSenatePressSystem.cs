using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Access.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Core;

/// <summary>Server-owned press identity and publication selection for the current round.</summary>
public sealed partial class SWSenatePressSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedIdCardSystem _idCards = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private readonly Dictionary<NetUserId, PressEdition> _editions = new();
    public event Action? PublicationsChanged;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetPress());
        SubscribeLocalEvent<SWSenateOfficeHolderChangedEvent>(OnPressOfficeChanged);
    }

    /// <summary>Press access follows the correspondent's equipped ID, like other Senate consoles.</summary>
    public bool TryGetCorrespondent(EntityUid actor, out NetUserId userId)
    {
        userId = default;
        if (!_time.ModeActive || !TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame || player.PlayerSession.AttachedEntity != actor
            || !TryComp<MobStateComponent>(actor, out var mob) || mob.CurrentState != MobState.Alive
            || !_mind.TryGetMind(actor, out _, out _)
            || !_idCards.TryFindIdCard(actor, out var card)
            || card.Comp.JobPrototype != "SWHolonetCorrespondent")
            return false;

        userId = player.PlayerSession.UserId;
        return true;
    }

    public bool TryGetEdition(EntityUid actor, out PressEdition edition)
    {
        edition = default;
        return TryGetCorrespondent(actor, out var userId) && _editions.TryGetValue(userId, out edition);
    }

    public bool SelectEdition(EntityUid actor, PressEdition edition)
    {
        if (!TryGetCorrespondent(actor, out var userId) || !Enum.IsDefined(edition)
            || _editions.ContainsKey(userId))
            return false;
        _editions[userId] = edition;
        PublicationsChanged?.Invoke();
        return true;
    }

    public bool LeaveEdition(EntityUid actor)
    {
        if (!TryGetCorrespondent(actor, out var userId) || !_editions.Remove(userId))
            return false;
        PublicationsChanged?.Invoke();
        return true;
    }
}

public enum PressEdition : byte
{
    RepublicHerald,
    VoicesOfRepublic,
    CoruscantTribune,
    GalacticObserver,
}
