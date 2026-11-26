using Content.Shared._SW._Senate;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.Work;

public sealed partial class SWSenateWorkConsoleMenu
{
    private void BuildSecuritySabotageApprovals(SWSenateWorkConsoleState state)
    {
        foreach (var request in state.SecuritySabotageRequests)
        {
            var card = SWConsoleStyle.Row(SWConsoleStyle.BgPanel, SWConsoleStyle.Warn);
            var content = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 5,
            };
            content.AddChild(SWConsoleStyle.Section("Диверсия на имперской территории"));
            content.AddChild(SWConsoleStyle.Wrap(request.Title, SWConsoleStyle.TextBright));
            content.AddChild(SWConsoleStyle.Wrap(request.WorldName, SWConsoleStyle.Accent));
            content.AddChild(SWConsoleStyle.Wrap($"Группа {request.TeamId}    {request.Cost} кр из бюджета МВБ", SWConsoleStyle.TextDim));
            content.AddChild(SWConsoleStyle.Wrap(request.Risk, SWConsoleStyle.Warn));
            var actions = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6 };
            var approve = SWConsoleStyle.Action("Одобрить диверсию", SWConsoleStyle.Warn, !state.CanApproveSecuritySabotage);
            var reject = SWConsoleStyle.Action("Отклонить", SWConsoleStyle.Neutral, !state.CanApproveSecuritySabotage);
            var caseId = request.CaseId;
            approve.OnPressed += _ =>
            {
                _owner.SecuritySabotageDecision(caseId, true);
                approve.Disabled = reject.Disabled = true;
            };
            reject.OnPressed += _ =>
            {
                _owner.SecuritySabotageDecision(caseId, false);
                approve.Disabled = reject.Disabled = true;
            };
            actions.AddChild(approve);
            actions.AddChild(reject);
            content.AddChild(actions);
            card.AddChild(content);
            WarApprovalBox.AddChild(card);
        }
    }
}
