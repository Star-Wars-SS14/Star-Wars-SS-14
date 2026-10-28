using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.Popups;

namespace Content.Server._SW._Senate.Consoles;

public sealed partial class SWMinistryWorkConsoleSystem
{
    private void InitializeJusticeInspections()
    {
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeOpenInspectionMessage>(OnOpenInspection);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeRunCheckMessage>(OnRunInspectionCheck);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeConcludeMessage>(OnInspectionConclusion);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeRemedyMessage>(OnInspectionRemedy);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeFollowUpMessage>(OnInspectionFollowUp);
    }

    private bool CanActAsJustice(Entity<SWMinistryWorkConsoleComponent> ent, EntityUid actor) =>
        _time.ModeActive && _time.DayActive && IsJusticeCard(ent)
        && _ministry.IsActiveHolder(SWMinistryJusticeSystem.JusticeMinistryId, actor);

    private void InspectionResult(Entity<SWMinistryWorkConsoleComponent> ent, EntityUid actor, bool accepted)
    {
        if (!accepted)
            _popup.PopupCursor("Действие недоступно. Проверьте материалы дела, занятость инспекторов и бюджет.", actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnOpenInspection(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeOpenInspectionMessage args)
    {
        if (!CanActAsJustice(ent, args.Actor)) return;
        InspectionResult(ent, args.Actor, _justice.OpenInspection(args.InstitutionId));
    }

    private void OnRunInspectionCheck(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeRunCheckMessage args)
    {
        if (!CanActAsJustice(ent, args.Actor)) return;
        InspectionResult(ent, args.Actor, _justice.RunCheck(args.CaseId, args.CheckId));
    }

    private void OnInspectionConclusion(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeConcludeMessage args)
    {
        if (!CanActAsJustice(ent, args.Actor)) return;
        InspectionResult(ent, args.Actor, _justice.Conclude(args.CaseId, args.Conclusion, args.EvidenceIds));
    }

    private void OnInspectionRemedy(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeRemedyMessage args)
    {
        if (!CanActAsJustice(ent, args.Actor)) return;
        InspectionResult(ent, args.Actor, _justice.ApplyRemedy(args.CaseId, args.Remedy));
    }

    private void OnInspectionFollowUp(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeFollowUpMessage args)
    {
        if (!CanActAsJustice(ent, args.Actor)) return;
        InspectionResult(ent, args.Actor, _justice.FollowUp(args.CaseId));
    }
}
