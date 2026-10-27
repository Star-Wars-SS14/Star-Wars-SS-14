using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWHealthNetworkPanel
{
    private void BuildDevelopment()
    {
        _development.RemoveAllChildren();
        var world = _health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId);
        if (world == null) return;
        foreach (var program in world.Programs)
        {
            var card = SWConsoleStyle.Row();
            var body = Stack();
            body.AddChild(SWConsoleStyle.Wrap(program.Name, SWConsoleStyle.Brass));
            body.AddChild(SWConsoleStyle.Wrap(program.Status, program.Applied ? SWConsoleStyle.Good : SWConsoleStyle.TextBright));
            var busy = program.Started || program.Applied;
            var action = program.Id == "SWProgramClinics" ? SWHealthAction.BuildClinic : SWHealthAction.StartPrevention;
            var button = SWConsoleStyle.Action(busy ? "Состояние и снабжение" : $"Назначить за {program.Cost:N0} кр", SWConsoleStyle.Accent,
                !busy && (!_health.CanManage || !_health.MinisterActive || !program.Affordable));
            button.Name = "HealthProgram_" + program.Id;
            button.OnPressed += _ =>
            {
                if (busy) OpenSupply("program:" + world.Id + ":" + program.Id);
                else ActionRequested?.Invoke(new(action, world.Id));
            };
            body.AddChild(button);
            card.AddChild(body);
            _development.AddChild(card);
        }
        var relief = SWConsoleStyle.Row();
        var content = Stack();
        content.AddChild(SWConsoleStyle.Wrap("Медицинская помощь", SWConsoleStyle.Brass));
        content.AddChild(SWConsoleStyle.Wrap($"Запас помощи: {world.ReserveHours:0.#} ч", SWConsoleStyle.TextBright));
        var send = SWConsoleStyle.Action($"Заказать за {_health.AidCost:N0} кр", SWConsoleStyle.Accent,
            !_health.CanManage || !_health.MinisterActive || _health.Budget < _health.AidCost);
        send.Name = "HealthSendAid";
        send.OnPressed += _ => ActionRequested?.Invoke(new(SWHealthAction.SendAid, world.Id));
        content.AddChild(send);
        relief.AddChild(content);
        _development.AddChild(relief);
    }
}
