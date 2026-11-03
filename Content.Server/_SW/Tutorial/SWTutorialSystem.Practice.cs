using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.Lathe.SWLathe;
using Content.Server.Radio;
using Content.Server.Power.Components;
using Content.Shared._SW.Tutorial;
using Content.Shared.Radio.Components;
using Robust.Shared.Player;

namespace Content.Server._SW.Tutorial;

public sealed partial class SWTutorialSystem
{
    [Dependency] private readonly SWGlobalWarMarketSystem _workMarket = default!;

    private void InitializePractice()
    {
        InitializeSpecializations();
        EntityManager.EntityInitialized += OnPracticeOutput;
        SubscribeLocalEvent<ActiveRadioComponent, RadioReceiveEvent>(OnPracticeRadioReceived);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnPracticeRadioAttempt);
    }

    private void OnPracticeRadioAttempt(ref RadioReceiveAttemptEvent args)
    {
        var source = Transform(args.RadioSource).MapUid;
        var destination = Transform(args.RadioReceiver).MapUid;
        // Tutorial radio can never leak between students or into the campaign, including global receivers.
        if (source != destination && _students.Values.Any(s => s.Map == source || s.Map == destination))
            args.Cancelled = true;
    }

    private void OnPracticeRadioReceived(Entity<ActiveRadioComponent> receiver, ref RadioReceiveEvent args)
    {
        if (!_player.TryGetSessionByEntity(args.MessageSource, out var session) ||
            !_students.TryGetValue(session.UserId, out var student) || student.Body != args.MessageSource ||
            Transform(receiver).MapUid != student.Map || Transform(args.RadioSource).MapUid != student.Map)
            return;
        if (CurrentStep(student).Completion is SWTutorialSaidOnRadioCondition condition && args.Channel.ID == condition.Channel)
            student.LastChannel = args.Channel.ID;
    }

    private bool InPractice(Student student, EntityUid entity) =>
        !TerminatingOrDeleted(entity) && Transform(entity).MapUid == student.Map;

    private bool IsContained(Student student, SWTutorialContainedCondition condition)
    {
        return GetTagged(student, condition.Tag).Any(item => InPractice(student, item) &&
            _containers.TryGetContainingContainer(item, out var container) &&
            GetTagged(student, condition.ContainerTag).Any(target => InPractice(student, target) && container.Owner == target));
    }

    private void OnPracticeOutput(Entity<MetaDataComponent> output)
    {
        if (output.Comp.EntityPrototype?.ID != "SWElectronics1")
            return;
        var transform = Transform(output);
        foreach (var student in _students.Values)
        {
            if (!student.Scenario.GuidedPractice || transform.MapUid != student.Map)
                continue;
            foreach (var machine in GetTagged(student, "machine"))
            {
                // SWLathe spawns its actual products before clearing CurrentRecipeProducting. Observing this
                // event also works during a disconnect and cannot miss a short production cycle.
                if (!InPractice(student, machine) || !TryComp<SWLatheComponent>(machine, out var lathe) ||
                    lathe.CurrentRecipeProducting?.ID != "SWTutorialElectronicsRecipe" ||
                    !_transform.InRange(transform.Coordinates, Transform(machine).Coordinates, 2f))
                    continue;
                if (!student.Tagged.TryGetValue("product", out var products))
                    student.Tagged["product"] = products = new();
                products.Add(output.Owner);
                return;
            }
        }
    }

    private bool IsPracticePowered(Student student, SWTutorialPoweredCondition condition)
    {
        return GetTagged(student, condition.SupplyTag).Any(supply => InPractice(student, supply) &&
                   TryComp<ApcComponent>(supply, out var apc) && apc.MainBreakerEnabled) &&
               GetTagged(student, condition.Tag).Any(machine => InPractice(student, machine) &&
                   TryComp<ApcPowerReceiverComponent>(machine, out var receiver) && receiver.NeedsPower && receiver.Powered);
    }

    private void OfferLiveWork(ICommonSession session)
    {
        var work = _workMarket.GetLocalWorkSuggestions(session).FirstOrDefault();
        if (work == null)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("sw-tutorial-live-no-work"));
            return;
        }
        // A suggestion is not a claim or a completed delivery. The ordinary board rechecks goods and authority.
        _chat.DispatchServerMessage(session, Loc.GetString("sw-tutorial-live-work",
            ("number", work.Number), ("available", work.Available), ("source", work.SourceName),
            ("recipient", work.RecipientName)));
    }
}
