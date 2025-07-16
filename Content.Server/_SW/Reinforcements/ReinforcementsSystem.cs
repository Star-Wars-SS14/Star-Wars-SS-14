using Content.Server._SW.Reinforcements.Components;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;

namespace Content.Server._SW.Reinforcements;

public sealed class ReinforcementsSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWReinforcementsCardComponent, AfterInteractEvent>(AfterInteract);
        SubscribeLocalEvent<CommunicationsConsoleComponent, ReinforcementsBroadcastMessage>(OnReinforcementsCall);
    }

    private void AfterInteract(EntityUid card, SWReinforcementsCardComponent component, AfterInteractEvent args)
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

        _chatSystem.DispatchGlobalAnnouncement($"Зафиксирована попытка {locInfo["status"]} кнопки вызова подкреплений с консоли управления {locInfo["fractionLoc"]}! Информация на проверке.", "Система общего взаимодействия 'Сатурн-09'", colorOverride: Color.IndianRed, announcementSound: sound);
    }

    private void OnReinforcementsCall(EntityUid console,
        CommunicationsConsoleComponent component,
        ReinforcementsBroadcastMessage args)
    {
        if(component.CanReinforcements == false)
            return;

        if(component.ReinforcementsIsCalled)
            return;

        var locInfo = GetLocInfo(component);

        _chatSystem.DispatchGlobalAnnouncement($"Зафиксирована попытка запроса подкреплений с консоли управления {locInfo["fractionLoc"]}. Информация уточняется..", "Система общего взаимодействия 'Сатурн-09'", colorOverride: Color.MediumVioletRed, announcementSound: component.ConsoleSoundReinforcements);
    }

    private Dictionary<string, string> GetLocInfo(CommunicationsConsoleComponent communicationsConsoleComponent,
        SWReinforcementsCardComponent? component = null)

    {
        var status = communicationsConsoleComponent.CanReinforcements ? "активации" : "деактивации";
        Dictionary<StarWarsFractionType, string> fractionLoc = new()
        {
            { StarWarsFractionType.Empire, "Империи" },
            { StarWarsFractionType.SWRepublic, "Республики" },
            { StarWarsFractionType.SWMandalorian, "Мандалорцев" }
        };

        return new Dictionary<string, string>
        {
            { "status", status },
            { "fractionLoc", fractionLoc[component?.FractionType ?? communicationsConsoleComponent.SwFractionType] }
        };
    }
}
