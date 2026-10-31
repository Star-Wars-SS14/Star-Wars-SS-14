using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Core;

/// <summary>Server-owned press identity and publication selection for the current round.</summary>
public sealed partial class SWSenatePressSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private readonly Dictionary<NetUserId, PressEdition> _editions = new();
    public event Action? PublicationsChanged;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetPress());
        SubscribeLocalEvent<SWSenateOfficeHolderChangedEvent>(OnPressOfficeChanged);
    }

    /// <summary>Checks the attached, alive character and its actual job, never its ID card or name.</summary>
    public bool TryGetCorrespondent(EntityUid actor, out NetUserId userId)
    {
        userId = default;
        if (!_time.ModeActive || !TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame || player.PlayerSession.AttachedEntity != actor
            || !TryComp<MobStateComponent>(actor, out var mob) || mob.CurrentState != MobState.Alive
            || !_mind.TryGetMind(actor, out var mind, out _)
            || !_jobs.MindTryGetJob(mind, out var job) || job.ID != "SWHolonetCorrespondent")
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
        if (!TryGetCorrespondent(actor, out var userId) || !Enum.IsDefined(edition))
            return false;
        _editions[userId] = edition;
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
