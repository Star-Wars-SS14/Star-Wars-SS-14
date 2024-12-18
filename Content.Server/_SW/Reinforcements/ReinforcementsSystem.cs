using Content.Server._SW.Reinforcements.Components;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._SW.Reinforcements;

public sealed class ReinforcementsSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReinforcementsCardComponent, AfterInteractEvent>(AfterInteract);
        SubscribeLocalEvent<CommunicationsConsoleComponent, ReinforcementsBroadcastMessage>(OnReinforcementsCall);
    }

    private void AfterInteract(EntityUid card, ReinforcementsCardComponent component, AfterInteractEvent args)
    {
        if(!args.Target.HasValue)
            return;

        var target = args.Target;

        if(!TryComp<CommunicationsConsoleComponent>(target, out var communicationsConsoleComponent))
            return;

        if(communicationsConsoleComponent.ReinforcementsIsCalled)
            return;

        if (communicationsConsoleComponent.SwFractionType != component.FractionType)
        {
            _popupSystem.PopupEntity("Отказано в доступе!", args.User, args.User, PopupType.MediumCaution);
            return;
        }

        communicationsConsoleComponent.CanReinforcements = !communicationsConsoleComponent.CanReinforcements;
        var sound = communicationsConsoleComponent.CanReinforcements ? component.ConsoleSoundActivated : component.ConsoleSoundDeActivated;
        var locInfo = GetLocInfo(communicationsConsoleComponent, component);

        _chatSystem.DispatchGlobalAnnouncement($"Консоль управления {locInfo["fractionLoc"]} {locInfo["status"]} кнопку вызова подкреплений!", "Система общего взаимодействия 'Сатурн-09'", colorOverride: Color.IndianRed, announcementSound: sound);
    }

    private void OnReinforcementsCall(EntityUid console,
        CommunicationsConsoleComponent component,
        ReinforcementsBroadcastMessage args)
    {
        if(component.CanReinforcements == false)
            return;

        if(component.ReinforcementsIsCalled)
            return;


    }

    private Dictionary<string, string> GetLocInfo(CommunicationsConsoleComponent communicationsConsoleComponent, ReinforcementsCardComponent component)
    {
        var status = communicationsConsoleComponent.CanReinforcements ? "активировала" : "деактивировала";
        Dictionary<StarWarsFractionType, string> fractionLoc = new()
        {
            { StarWarsFractionType.Empire, "Империи" },
            { StarWarsFractionType.Republic, "Республики" },
            { StarWarsFractionType.Mandalorian, "Мандалорцев" }
        };

        return new Dictionary<string, string>
        {
            {"status", status},
            {"fractionLoc", fractionLoc[component.FractionType]}
        };
    }
}
