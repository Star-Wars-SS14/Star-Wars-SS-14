using System;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

internal static class SWSecurityDossierPresentation
{
    public static void Add(BoxContainer host, SWSecurityCaseView dossier, string world, bool cluesOpen, Action toggleClues)
    {
        host.AddChild(SWConsoleStyle.Wrap(dossier.Status, SWConsoleStyle.TextBright));
        if (!string.IsNullOrWhiteSpace(dossier.Briefing))
            host.AddChild(SWConsoleStyle.Wrap(dossier.Briefing, SWConsoleStyle.TextDim));
        if (!string.IsNullOrWhiteSpace(dossier.Scene))
        {
            host.AddChild(SWConsoleStyle.Section($"Этап {Math.Min(dossier.Evidence + 1, Math.Max(1, dossier.RequiredEvidence))}"));
            host.AddChild(SWConsoleStyle.Wrap(dossier.Scene, SWConsoleStyle.TextBright));
        }
        if (!string.IsNullOrWhiteSpace(dossier.Objective))
            host.AddChild(SWConsoleStyle.Wrap(dossier.Objective, SWConsoleStyle.TextDim));
        host.AddChild(SWConsoleStyle.Wrap($"Место расследования: {world}", SWConsoleStyle.Accent));
        host.AddChild(SWConsoleStyle.Bar(dossier.Evidence, 0, Math.Max(1, dossier.RequiredEvidence), SWConsoleStyle.Accent));
        if (dossier.Clues.Count > 0)
        {
            var clues = SWConsoleStyle.TabButton($"{(cluesOpen ? "Скрыть" : "Показать")} улики ({dossier.Clues.Count})");
            clues.OnPressed += _ => toggleClues();
            host.AddChild(clues);
            if (cluesOpen)
                foreach (var clue in dossier.Clues) host.AddChild(SWConsoleStyle.Wrap(clue, SWConsoleStyle.TextDim));
        }
        if (dossier.Kind == SWSecurityCaseKind.Imperial)
            host.AddChild(SWConsoleStyle.Wrap(dossier.SabotageApprovalPending ? "Ожидается решение Канцлера" : dossier.SabotageApproved ? "Диверсия одобрена; подтвердите запуск" : "Диверсия требует одобрения Канцлера", SWConsoleStyle.Brass));
    }
}
