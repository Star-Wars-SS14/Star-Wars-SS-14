using Content.Shared._SW.Tutorial;
using Robust.Shared.Player;

namespace Content.Server._SW.Tutorial;

public sealed partial class SWTutorialSystem
{
    private static string CheckpointKey(SWTutorialScenarioPrototype scenario, string step) =>
        $"SWTutorialV{scenario.Version}:{scenario.ID}:{step}";

    private static string CurrentCheckpointKey(SWTutorialScenarioPrototype scenario) =>
        $"SWTutorialV{scenario.Version}:{scenario.ID}:Current";

    private void SaveCheckpoint(ICommonSession session, SWTutorialScenarioPrototype scenario, int index)
    {
        var key = CurrentCheckpointKey(scenario);
        _playTime.TryGetTrackerTime(session, key, out var previous);
        // This private tracker encodes the current checkpoint, not earned playtime or a role entitlement.
        // Replacing it also makes an explicit restart resume from the new attempt rather than an old later flag.
        _playTime.AddTimeToTracker(session, key, TimeSpan.FromSeconds(index + 1) - (previous ?? TimeSpan.Zero));
        _playTime.SaveSession(session);
    }

    private int FindCheckpoint(ICommonSession session, SWTutorialScenarioPrototype scenario)
    {
        if (_playTime.TryGetTrackerTime(session, CurrentCheckpointKey(scenario), out var current))
        {
            var index = (int) current.Value.TotalSeconds - 1;
            return index >= 0 && index < scenario.Steps.Count && _proto.Index(scenario.Steps[index]).Checkpoint
                ? index : 0;
        }
        // Migrate the earlier version's presence flags on the first resumed checkpoint.
        for (var i = scenario.Steps.Count - 1; i > 0; i--)
        {
            var step = _proto.Index(scenario.Steps[i]);
            if (step.Checkpoint && _playTime.TryGetTrackerTime(session, CheckpointKey(scenario, step.ID), out _))
                return i;
        }
        return 0;
    }

    private void OnHelp(SWTutorialHelpEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_students.TryGetValue(session.UserId, out var student) || student.DisconnectedAt != null ||
            student.Attempt != ev.Attempt || student.Revision != ev.Revision)
            return;

        if (ev.Retry)
        {
            // Each checkpoint reconstructs its own finite exercise. All prior scene entities are removed first.
            foreach (var entities in student.Tagged.Values)
                foreach (var entity in entities)
                    if (!TerminatingOrDeleted(entity))
                        Del(entity);
            student.Tagged.Clear();
            foreach (var entity in student.UntilStepEnd)
                if (!TerminatingOrDeleted(entity))
                    Del(entity);
            student.UntilStepEnd.Clear();
            var index = student.StepIndex;
            while (index > 0 && !_proto.Index(student.Scenario.Steps[index]).Checkpoint)
                index--;
            RunActions(student, student.Scenario.Setup);
            StartStep(session, student, index);
            Funnel(student, "exercise retry");
        }
    }
}
