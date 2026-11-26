using Content.Server._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Reads the final spawn validation, after every subscriber that may refuse it has spoken.</summary>
public sealed class SWGlobalWarAdmissionFeedbackSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarSpawnSystem _spawn = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarValidateSpawnEvent>(OnValidated,
            after: [typeof(SWGlobalWarSpawnSystem), typeof(SWGlobalWarAssignmentsSystem)]);
    }

    private void OnValidated(ref SWGlobalWarValidateSpawnEvent args)
    {
        if (args.Cancelled)
            _spawn.NoteRefusal(args.Player, args.Reason);
    }
}
