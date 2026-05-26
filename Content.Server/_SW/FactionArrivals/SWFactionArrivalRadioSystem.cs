using Content.Server.GameTicking.Rules;
using Content.Server.Radio.EntitySystems;
using Content.Server._SW.TaskSystem;
using Content.Shared._SW.TaskSystem;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.FactionArrivals;

public sealed class SWFactionArrivalRadioSystem : GameRuleSystem<SWFactionArrivalRadioRuleComponent>
{
    [Dependency] private readonly SWFactionGameruleSystem _factionRule = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var rules = QueryActiveRules();
        while (rules.MoveNext(out _, out _, out var component, out _))
        {
            if (component.SkipSilent && args.Silent)
                continue;

            foreach (var announcement in component.Announcements)
            {
                if (!MatchesFaction(args.Mob, args.JobId, announcement.Faction))
                    continue;

                AnnounceArrival(args.Mob, args.JobId, announcement);
            }
        }
    }

    private void AnnounceArrival(EntityUid mob, string? jobId, SWFactionArrivalRadioAnnouncement announcement)
    {
        if (Deleted(mob) || !MatchesFaction(mob, jobId, announcement.Faction))
            return;

        var jobName = jobId;
        if (jobId != null && _prototype.TryIndex<JobPrototype>(jobId, out var job))
            jobName = job.LocalizedName;

        var factionName = announcement.Faction;
        if (_factionRule.GetFactionEntity(announcement.Faction) is { } faction)
            factionName = faction.Comp.Name;

        var message = Loc.GetString(announcement.Phrase,
            ("character", MetaData(mob).EntityName),
            ("entity", mob),
            ("faction", factionName),
            ("job", jobName ?? Loc.GetString("sw-faction-arrival-radio-unknown-job")));

        _radio.SendRadioMessage(mob, message, announcement.Channel, mob);
    }

    private bool MatchesFaction(EntityUid mob, string? jobId, string faction)
    {
        if (TryComp<SWFactionMemberComponent>(mob, out var factionMember) &&
            factionMember.Faction == faction)
        {
            return true;
        }

        if (jobId == null)
            return false;

        var factionEntity = _factionRule.GetFactionEntity(faction);
        return factionEntity?.Comp.RelatedJobs.Contains(jobId) == true;
    }
}
