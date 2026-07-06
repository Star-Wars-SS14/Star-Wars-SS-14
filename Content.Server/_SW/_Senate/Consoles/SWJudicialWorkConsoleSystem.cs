using System.Linq;
using Content.Server.Popups;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWJudicialWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly SWSenateAmbitionSystem _ambition = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWCourtVerdictVoteMessage>(OnVerdictVote);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWCourtCloseVoteMessage>(OnCloseVote);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWCourtStartReviewMessage>(OnStartReview);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWCourtReviewVoteMessage>(OnReviewVote);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWCourtResignMessage>(OnResign);
        SubscribeLocalEvent<SWJudicialWorkConsoleComponent, SWSenateTakeOfficeMessage>(OnTakeOffice);
    }

    private void OnOpened(EntityUid uid, SWJudicialWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        // Ghost-role dummies spawn with a nameless judicial card: bind it to whoever first opens
        // the console with it so the bench can register them by name.
        var card = _itemSlots.GetItemOrNull(uid, comp.IdSlot);
        if (card != null && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position is SWSenatePosition.ChiefJustice or SWSenatePosition.Judge
                or SWSenatePosition.Prosecutor or SWSenatePosition.Defender
            && TryComp<IdCardComponent>(card, out var idCard)
            && string.IsNullOrWhiteSpace(idCard.FullName))
        {
            _idCard.TryChangeFullName(card.Value, Name(args.Actor));
        }

        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWJudicialWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWJudicialWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private (SWSenatePosition Position, string Name)? GetCardIdentity(Entity<SWJudicialWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
            return null;

        var fullName = TryComp<IdCardComponent>(card, out var idCard) && !string.IsNullOrWhiteSpace(idCard.FullName)
            ? idCard.FullName
            : null;

        switch (position.Position)
        {
            case SWSenatePosition.ChiefJustice:
            case SWSenatePosition.Judge:
            case SWSenatePosition.Prosecutor:
            case SWSenatePosition.Defender:
                return fullName == null ? null : (position.Position, fullName);

            default:
                // A freshly appointed judge arrives with their old card: convert it once the
                // bench actually lists them.
                if (fullName == null || !_court.IsJudge(fullName))
                    return null;

                position.Position = _court.Chief == fullName ? SWSenatePosition.ChiefJustice : SWSenatePosition.Judge;
                if (TryComp<SWSenateSectorComponent>(card, out var sector))
                    sector.Sector = null;
                if (TryComp<SWMinistryComponent>(card, out var ministry))
                    ministry.Ministry = null;
                return (position.Position, fullName);
        }
    }

    private (SWSenatePosition Position, string Name)? GetIdentity(Entity<SWJudicialWorkConsoleComponent> ent)
    {
        if (GetCardIdentity(ent) is not { } id)
            return null;

        return IsOfficeHolder(id.Position, id.Name) ? id : null;
    }

    private bool IsOfficeHolder(SWSenatePosition position, string name) => position switch
    {
        SWSenatePosition.ChiefJustice => _court.Chief == name,
        SWSenatePosition.Judge => _court.Judges.Contains(name),
        SWSenatePosition.Prosecutor => _court.Prosecutor == name,
        SWSenatePosition.Defender => _court.Defender == name,
        _ => false,
    };

    private static string RoleLabel(SWSenatePosition position, string name) => position switch
    {
        SWSenatePosition.ChiefJustice => $"Верховный судья {name}",
        SWSenatePosition.Judge => $"Республиканский судья {name}",
        SWSenatePosition.Prosecutor => $"Обвинитель {name}",
        SWSenatePosition.Defender => $"Защитник {name}",
        _ => name,
    };

    private void OnTakeOffice(Entity<SWJudicialWorkConsoleComponent> ent, ref SWSenateTakeOfficeMessage args)
    {
        if (GetCardIdentity(ent) is not { } id)
        {
            _popup.PopupCursor("Вставьте судебную карту.", args.Actor, PopupType.Medium);
            return;
        }

        if (id.Name != Name(args.Actor))
        {
            _popup.PopupCursor("Эта карта принадлежит другому лицу.", args.Actor, PopupType.Medium);
            return;
        }

        if (_court.TakeOffice(id.Position, id.Name))
            _popup.PopupCursor("Судебные полномочия приняты.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Принять судебные полномочия невозможно.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnVerdictVote(Entity<SWJudicialWorkConsoleComponent> ent, ref SWCourtVerdictVoteMessage args)
    {
        if (GetIdentity(ent) is not { } id || !_court.IsJudge(id.Name) || id.Name != Name(args.Actor))
            return;

        if (_court.CastVerdictVote(id.Name, args.CaseId, args.ForPlaintiff))
            _popup.PopupCursor("Голос учтён.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCloseVote(Entity<SWJudicialWorkConsoleComponent> ent, ref SWCourtCloseVoteMessage args)
    {
        if (GetIdentity(ent) is not { } id || !_court.IsJudge(id.Name) || id.Name != Name(args.Actor))
            return;

        if (_court.CloseVerdictVote(id.Name, args.CaseId))
            _popup.PopupCursor("Голосование закрыто.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Закрыть голосование нельзя.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnStartReview(Entity<SWJudicialWorkConsoleComponent> ent, ref SWCourtStartReviewMessage args)
    {
        if (GetIdentity(ent) is not { } id || id.Name != Name(args.Actor))
            return;

        if (_court.StartReview(id.Name, args.LawId))
            _popup.PopupCursor("Проверка закона открыта.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Проверку открыть нельзя.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnReviewVote(Entity<SWJudicialWorkConsoleComponent> ent, ref SWCourtReviewVoteMessage args)
    {
        if (GetIdentity(ent) is not { } id || !_court.IsJudge(id.Name) || id.Name != Name(args.Actor))
            return;

        _court.CastReviewVote(id.Name, args.Unconstitutional);
        UpdateUi(ent);
    }

    private void OnResign(Entity<SWJudicialWorkConsoleComponent> ent, ref SWCourtResignMessage args)
    {
        if (GetIdentity(ent) is not { } id || id.Name != Name(args.Actor))
            return;

        if (!_court.Resign(id.Name))
            return;

        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card != null && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position is SWSenatePosition.ChiefJustice or SWSenatePosition.Judge)
            position.Position = SWSenatePosition.Senator;

        _popup.PopupCursor("Полномочия сложены.", args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWJudicialWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWJudicialWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWJudicialWorkConsoleComponent> ent)
    {
        var state = new SWJudicialWorkConsoleState
        {
            GameTime = _time.TimeString(),
            BenchLabel = _court.BenchLabel(),
            Judges = _court.BuildJudges(),
            ProsecutorName = _court.Prosecutor ?? string.Empty,
            DefenderName = _court.Defender ?? string.Empty,
        };

        if (GetCardIdentity(ent) is { } cardId)
        {
            state.HasCard = true;
            state.RoleLabel = RoleLabel(cardId.Position, cardId.Name);
            state.InOffice = IsOfficeHolder(cardId.Position, cardId.Name);
            state.CanTakeOffice = !state.InOffice && _court.CanTakeOffice(cardId.Position, cardId.Name);
            state.TakeOfficeLabel = $"Карта подтверждена: {state.RoleLabel}.";
        }

        if (GetIdentity(ent) is { } id)
        {
            state.IsJudge = _court.IsJudge(id.Name);
            state.IsChief = _court.Chief == id.Name;

            var hintSide = id.Position switch
            {
                SWSenatePosition.Prosecutor => 1,
                SWSenatePosition.Defender => 2,
                _ => 0,
            };

            state.CurrentCase = _court.BuildCurrentCase(id.Name, hintSide);
            state.Registry = _court.BuildRegistry(id.Name, hintSide);
            state.Review = _court.BuildReview(id.Name);
            if (state.IsChief && state.Review == null)
                state.ReviewableLaws = _senate.BuildRepealableLaws();
            state.Notices = _court.GetNotices(id.Name);

            var judgeIndex = -1;
            for (var i = 0; i < _court.Judges.Count; i++)
            {
                if (_court.Judges[i] == id.Name)
                {
                    judgeIndex = i;
                    break;
                }
            }

            var ambitionOwner = id.Position switch
            {
                SWSenatePosition.ChiefJustice => "court-chief",
                SWSenatePosition.Prosecutor => "court-prosecutor",
                SWSenatePosition.Defender => "court-defender",
                _ => judgeIndex >= 0 ? $"court-judge-{judgeIndex}" : null,
            };
            if (ambitionOwner != null)
                state.Ambitions = _ambition.BuildAmbitions(ambitionOwner);
        }

        _ui.SetUiState(ent.Owner, SWJudicialWorkConsoleUiKey.Key, state);
    }
}
