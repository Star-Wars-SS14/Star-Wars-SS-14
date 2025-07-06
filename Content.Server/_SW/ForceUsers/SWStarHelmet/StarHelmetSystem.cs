using Content.Server._SW.Force.Components;
using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Inventory.Events;
using Content.Shared.Radio;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed class StarHelmetSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly RadioDeviceSystem _radioDevice = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StarHelmetComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<StarHelmetComponent, GotUnequippedEvent>(OnGotUnequipped);

        SubscribeLocalEvent<WearingStarHelmetComponent, EntitySpokeEvent>(OnSpeak);
        SubscribeLocalEvent<StarHelmetComponent, RadioReceiveEvent>(OnHelmetReceive);

        SubscribeLocalEvent<StarHelmetComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid helmet, StarHelmetComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || component.Channels.Count == 0)
            return;

        args.PushMarkup("Доступные каналы связи:");

        foreach (var id in component.Channels)
        {
            var proto = _prototypeManager.Index<RadioChannelPrototype>(id);

            var key = id == SharedChatSystem.CommonChannel
                ? SharedChatSystem.RadioCommonPrefix.ToString()
                : $"{SharedChatSystem.RadioChannelPrefix}{proto.KeyCode}";

            args.PushMarkup(Loc.GetString("examine-starhelmet",
                ("color", proto.Color),
                ("key", key),
                ("id", proto.LocalizedName),
                ("freq", proto.Frequency)));
        }
    }

    private void OnHelmetReceive(EntityUid helmet, StarHelmetComponent component, RadioReceiveEvent args)
    {
        if (TryComp(Transform(helmet).ParentUid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    private void OnSpeak(EntityUid messageSource, WearingStarHelmetComponent component, EntitySpokeEvent args)
    {
        if (component.ObjectsWithStarComponent.Count == 0)
        {
            return;
        }

        foreach (EntityUid helmet in component.ObjectsWithStarComponent)
        {
            if (args.Channel != null && HasComp<ActiveRadioComponent>(helmet))
            {
                _radio.SendRadioMessage(messageSource, args.Message, args.Channel, helmet);
                args.Channel = null;
            }
        }
    }

    private void OnGotEquipped(EntityUid helmet, StarHelmetComponent component, GotEquippedEvent args)
    {

        if (component.RequiredSlot != args.SlotFlags)
        {
            return;
        }
        WearingStarHelmetComponent comp = EnsureComp<WearingStarHelmetComponent>(args.Equipee);
        if (!comp.ObjectsWithStarComponent.Contains(helmet))
        {
            comp.ObjectsWithStarComponent.Add(helmet);
        }
        EnsureComp<ActiveRadioComponent>(helmet).Channels = new(component.Channels);

        EnsureComp<ActiveRadioComponent>(helmet).Channels = new HashSet<string>(component.Channels);


        if (!TryComp<RadioSpeakerComponent>(helmet, out var radioSpeakerComponent))
            return;

        if (radioSpeakerComponent.Enabled)
            _radioDevice.ToggleRadioSpeaker(helmet, helmet);
    }

    private void OnGotUnequipped(EntityUid helmet, StarHelmetComponent component, GotUnequippedEvent args)
    {
        RemComp<ActiveRadioComponent>(helmet);
        if (!TryComp<WearingStarHelmetComponent>(args.Equipee, out var wearingComponent))
        {
            return;
        }

        wearingComponent.ObjectsWithStarComponent.Remove(helmet);
        if (wearingComponent.ObjectsWithStarComponent.Count == 0)
        {
            RemComp<WearingStarHelmetComponent>(args.Equipee);
        }

        if (!TryComp<RadioSpeakerComponent>(helmet, out var radioSpeakerComponent))
            return;

        if (!radioSpeakerComponent.Enabled)
            _radioDevice.ToggleRadioSpeaker(helmet, helmet);
    }
}
