using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.GameTicking;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Antag;

public sealed class SWMercSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;

    private const int WindowOpenHour = 10;
    private const int WindowCloseHour = 18;
    private const float SecuritySoft = 40f;
    private const float SecurityHard = 25f;
    private const float AutoChance = 0.05f;
    private const float AutoChanceHot = 0.10f;
    private const float EvacHoldSeconds = 60f;

    private const int PriceProtectSelf = 800;
    private const int PriceProtectOther = 1000;
    private const int PriceKidnap = 1500;
    private const int PriceKill = 2000;
    private const float DirtyCorruption = 15f;
    private const float ProtectCorruption = 5f;

    private sealed class Syndicate
    {
        public string Name = string.Empty;
        public string Spawner = string.Empty;
    }

    private static readonly Syndicate[] Syndicates =
    {
        new() { Name = "Чёрное Солнце", Spawner = "SWSpawnPointMercBlackSun" },
        new() { Name = "Обмен", Spawner = "SWSpawnPointMercExchange" },
    };

    private sealed class Contract
    {
        public SWMercContractKind Kind;
        public string? Client;
        public string Target = string.Empty;
        public string Syndicate = string.Empty;
        public bool Succeeded;
        public bool Failed;
        public bool Closed;
    }

    private Contract? _contract;
    private readonly List<Contract> _history = new();
    private readonly Dictionary<string, int> _spent = new();
    private bool _autoSpawned;
    private bool _mercArrived;
    private int _lastProcHour = -1;
    private float _holdTime;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        SubscribeLocalEvent<SWMercMemberComponent, PlayerAttachedEvent>(OnMercAttached);
        SubscribeLocalEvent<SWShadowTerminalComponent, SWShadowHireMessage>(OnHire);
        SubscribeLocalEvent<SWShadowTerminalComponent, BeforeActivatableUIOpenEvent>(OnTerminalOpen);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _contract = null;
        _history.Clear();
        _spent.Clear();
        _autoSpawned = false;
        _mercArrived = false;
        _lastProcHour = -1;
        _holdTime = 0f;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        TickAuto();
        TickContract(frameTime);
    }

    private bool ContractOpen => _contract is { Closed: false };

    private void TickAuto()
    {
        if (_autoSpawned || ContractOpen || !_time.DayActive)
            return;

        var hour = _time.AbsoluteHour;
        if (hour == _lastProcHour)
            return;

        _lastProcHour = hour;

        var startDay = _time.Day;
        var roundDays = 3;
        var ruleQuery = EntityQueryEnumerator<SWSenateRuleComponent>();
        if (ruleQuery.MoveNext(out var rule))
        {
            startDay = rule.StartDay;
            roundDays = rule.RoundDays;
        }

        if (hour < startDay * 24 + WindowOpenHour)
            return;
        if (hour > (startDay + roundDays - 1) * 24 + WindowCloseHour)
            return;

        if (!_senate.GetRepublicGlobals().TryGetValue("SWGlobalSecurity", out var security) || security >= SecuritySoft)
            return;

        if (!_random.Prob(security < SecurityHard ? AutoChanceHot : AutoChance))
            return;

        var senators = _senate.SectorHolders.Values.Where(name => FindAlive(name) != null).ToList();
        if (senators.Count == 0 || !HasEvac() || !HasSpawns())
            return;

        var kind = _random.Prob(0.5f) ? SWMercContractKind.Kill : SWMercContractKind.Kidnap;
        if (StartContract(null, kind, _random.Pick(senators)))
            _autoSpawned = true;
    }

    private void TickContract(float frameTime)
    {
        if (_contract is not { Closed: false } contract)
            return;

        if (contract.Kind is SWMercContractKind.ProtectSelf or SWMercContractKind.ProtectOther)
        {
            var target = FindTarget(contract.Target, out var dead);
            if (target == null || dead)
            {
                contract.Failed = true;
                contract.Closed = true;
            }

            return;
        }

        if (_mercArrived && !AnyMercAlive())
        {
            contract.Failed = true;
            contract.Closed = true;
            return;
        }

        var wantDead = contract.Kind == SWMercContractKind.Kill;
        var victim = FindTarget(contract.Target, out var victimDead);
        if (victim == null || victimDead != wantDead || !InEvacZone(victim.Value))
        {
            _holdTime = 0f;
            return;
        }

        _holdTime += frameTime;
        if (_holdTime < EvacHoldSeconds)
            return;

        contract.Succeeded = true;
        contract.Closed = true;
        Announce(wantDead
            ? $"Тело — {contract.Target} — вывезено наёмником синдиката. След теряется за пределами планеты."
            : $"{contract.Target} похищен наёмником синдиката! След теряется за пределами планеты.");

        var members = EntityQueryEnumerator<SWMercMemberComponent>();
        while (members.MoveNext(out var uid, out _))
        {
            if (InEvacZone(uid))
                QueueDel(uid);
        }

        QueueDel(victim.Value);
    }

    public bool TryHire(string client, SWMercContractKind kind, string target, out string error)
    {
        error = string.Empty;

        if (ContractOpen)
        {
            error = "Канал занят: действующий контракт ещё не закрыт.";
            return false;
        }

        if (kind == SWMercContractKind.ProtectSelf)
            target = client;

        if (target.Length == 0)
        {
            error = "Не выбрана цель контракта.";
            return false;
        }

        if (kind != SWMercContractKind.ProtectSelf && target == client)
        {
            error = "Для контракта на себя выберите собственную защиту.";
            return false;
        }

        if (FindAlive(target) == null)
        {
            error = $"Цель не найдена или мертва: {target}";
            return false;
        }

        if (!HasSpawns())
        {
            error = "Исполнитель недоступен: нет точки прибытия (SWMercSpawn).";
            return false;
        }

        if (kind is SWMercContractKind.Kill or SWMercContractKind.Kidnap && !HasEvac())
        {
            error = "Исполнитель недоступен: нет канала эвакуации (SWMercEvacPoint).";
            return false;
        }

        var price = PriceFor(kind);
        if (!_corruption.SpendAccount(client, price))
        {
            error = $"Недостаточно средств на теневом счёте: нужно {price}.";
            return false;
        }

        _spent[client] = (_spent.TryGetValue(client, out var spent) ? spent : 0) + price;
        _corruption.AddPersonCorruption(client,
            kind is SWMercContractKind.Kill or SWMercContractKind.Kidnap ? DirtyCorruption : ProtectCorruption);

        if (!StartContract(client, kind, target))
        {
            error = "Исполнитель недоступен.";
            return false;
        }

        return true;
    }

    public bool TryOrder(SWMercContractKind kind, string target, out string error)
    {
        error = string.Empty;

        if (ContractOpen)
        {
            error = "Действующий контракт ещё не закрыт.";
            return false;
        }

        if (FindAlive(target) == null)
        {
            error = $"Цель не найдена или мертва: {target}";
            return false;
        }

        if (!HasSpawns())
        {
            error = "На карте нет маркеров прибытия (SWMercSpawn).";
            return false;
        }

        if (kind is SWMercContractKind.Kill or SWMercContractKind.Kidnap && !HasEvac())
        {
            error = "На карте нет точки эвакуации наёмника (SWMercEvacPoint).";
            return false;
        }

        if (!StartContract(null, kind, target))
        {
            error = "Не удалось запустить контракт.";
            return false;
        }

        return true;
    }

    private bool StartContract(string? client, SWMercContractKind kind, string target)
    {
        var spawns = new List<EntityCoordinates>();
        var spawnQuery = EntityQueryEnumerator<SWMercSpawnComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out _, out _, out var xform))
            spawns.Add(xform.Coordinates);

        if (spawns.Count == 0)
            return false;

        if (_contract is { } old && !_history.Contains(old))
            _history.Add(old);

        var syndicate = _random.Pick(Syndicates);
        _contract = new Contract
        {
            Kind = kind,
            Client = client,
            Target = target,
            Syndicate = syndicate.Name,
        };
        _holdTime = 0f;
        _mercArrived = false;

        Spawn(syndicate.Spawner, _random.Pick(spawns));
        return true;
    }

    public int PriceFor(SWMercContractKind kind)
    {
        var baseline = kind switch
        {
            SWMercContractKind.ProtectSelf => PriceProtectSelf,
            SWMercContractKind.ProtectOther => PriceProtectOther,
            SWMercContractKind.Kidnap => PriceKidnap,
            _ => PriceKill,
        };

        return (int) MathF.Round(baseline * _finance.PriceMultiplier() / 10f) * 10;
    }

    public string DescribeContract()
    {
        if (_contract is not { } c)
            return "-";

        var kind = KindLabel(c.Kind);
        return $"{kind} — {c.Target} ({c.Syndicate})";
    }

    public static string KindLabel(SWMercContractKind kind) => kind switch
    {
        SWMercContractKind.ProtectSelf => "защита заказчика",
        SWMercContractKind.ProtectOther => "защита",
        SWMercContractKind.Kill => "ликвидация",
        _ => "похищение",
    };

    public float PersonMetricValue(string name, string metric)
    {
        switch (metric)
        {
            case "DirtyDone":
                return AllContracts().Count(c =>
                    c.Client == name && c.Kind is SWMercContractKind.Kill or SWMercContractKind.Kidnap && c.Succeeded);
            case "SelfProtected":
                return AllContracts().Any(c =>
                    c.Client == name && c.Kind == SWMercContractKind.ProtectSelf && !c.Failed && FindAlive(name) != null)
                    ? 1f
                    : 0f;
            case "Spent":
                return _spent.TryGetValue(name, out var spent) ? spent : 0f;
            default:
                return 0f;
        }
    }

    private IEnumerable<Contract> AllContracts()
    {
        foreach (var c in _history)
            yield return c;
        if (_contract != null && !_history.Contains(_contract))
            yield return _contract;
    }

    private void OnMercAttached(EntityUid uid, SWMercMemberComponent comp, PlayerAttachedEvent args)
    {
        _mercArrived = true;
        if (_contract is not { } contract)
            return;

        var order = contract.Kind switch
        {
            SWMercContractKind.ProtectSelf or SWMercContractKind.ProtectOther =>
                $"Контракт: защита. {contract.Target} должен пережить раунд — контракт засчитывается, только если цель жива к финалу.",
            SWMercContractKind.Kill =>
                $"Контракт: ликвидация — {contract.Target}. Убейте цель и доставьте тело в точку эвакуации наёмника, удерживайте его там {(int) EvacHoldSeconds} секунд.",
            _ =>
                $"Контракт: похищение — {contract.Target}. Доставьте цель живой в точку эвакуации наёмника и удерживайте её там {(int) EvacHoldSeconds} секунд.",
        };

        var client = contract.Client != null ? $"Заказчик: {contract.Client}." : "Это собственная операция синдиката.";
        _chat.DispatchServerMessage(args.Player,
            $"Вы — наёмник синдиката «{contract.Syndicate}». {order} {client} Синдикат не прощает провалов и болтливости.");
    }

    private void OnHire(EntityUid uid, SWShadowTerminalComponent comp, SWShadowHireMessage args)
    {
        if (!_time.ModeActive)
            return;

        var client = Name(args.Actor);
        var notice = TryHire(client, args.Kind, args.Target.Trim(), out var error)
            ? "Контракт принят. Исполнитель прибудет в ближайшее время."
            : error;

        UpdateTerminal(uid, args.Actor, notice);
    }

    private void OnTerminalOpen(EntityUid uid, SWShadowTerminalComponent comp, BeforeActivatableUIOpenEvent args)
    {
        UpdateTerminal(uid, args.User, string.Empty);
    }

    private void UpdateTerminal(EntityUid uid, EntityUid user, string notice)
    {
        var name = Name(user);
        var state = new SWShadowTerminalState
        {
            UserName = name,
            Balance = _corruption.Account(name),
            ContractActive = ContractOpen,
            Notice = notice,
            PriceProtectSelf = PriceFor(SWMercContractKind.ProtectSelf),
            PriceProtectOther = PriceFor(SWMercContractKind.ProtectOther),
            PriceKill = PriceFor(SWMercContractKind.Kill),
            PriceKidnap = PriceFor(SWMercContractKind.Kidnap),
        };

        if (_contract is { Closed: false } contract)
        {
            state.ContractLabel = contract.Client == name
                ? $"Ваш контракт в работе: {KindLabel(contract.Kind)} — {contract.Target}."
                : "Исполнитель занят чужим контрактом.";
        }

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out _, out var mob, out var meta))
        {
            if (mob.CurrentState != MobState.Dead && meta.EntityName.Length > 0)
                state.Targets.Add(meta.EntityName);
        }

        state.Targets.Sort();
        _ui.SetUiState(uid, SWShadowTerminalUiKey.Key, state);
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        var contracts = AllContracts().ToList();
        if (contracts.Count == 0)
            return;

        args.AddLine(string.Empty);
        foreach (var contract in contracts)
        {
            if (contract.Kind is SWMercContractKind.ProtectSelf or SWMercContractKind.ProtectOther && !contract.Closed)
            {
                var target = FindTarget(contract.Target, out var dead);
                contract.Succeeded = target != null && !dead;
                contract.Failed = !contract.Succeeded;
                contract.Closed = true;
            }

            var result = contract.Succeeded
                ? "[color=red]контракт выполнен[/color]"
                : "[color=green]контракт провален[/color]";
            var client = contract.Client ?? "собственная операция синдиката";
            args.AddLine($"Наёмник «{contract.Syndicate}»: {KindLabel(contract.Kind)} — {contract.Target}; {result}. Заказчик: {client}.");
        }
    }

    private bool HasSpawns() => EntityQueryEnumerator<SWMercSpawnComponent>().MoveNext(out _);

    private bool HasEvac() => EntityQueryEnumerator<SWMercEvacPointComponent>().MoveNext(out _);

    private bool AnyMercAlive()
    {
        var query = EntityQueryEnumerator<SWMercMemberComponent, MobStateComponent>();
        while (query.MoveNext(out _, out _, out var mob))
        {
            if (mob.CurrentState != MobState.Dead)
                return true;
        }

        return false;
    }

    private EntityUid? FindAlive(string name)
    {
        var target = FindTarget(name, out var dead);
        return dead ? null : target;
    }

    private EntityUid? FindTarget(string name, out bool dead)
    {
        dead = false;
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var mob, out var meta))
        {
            if (meta.EntityName != name)
                continue;

            dead = mob.CurrentState == MobState.Dead;
            return uid;
        }

        return null;
    }

    private bool InEvacZone(EntityUid uid)
    {
        var xform = Transform(uid);
        var pos = _transform.GetWorldPosition(uid);
        var query = EntityQueryEnumerator<SWMercEvacPointComponent, TransformComponent>();
        while (query.MoveNext(out var evacUid, out var evac, out var evacXform))
        {
            if (evacXform.MapID != xform.MapID)
                continue;

            if ((_transform.GetWorldPosition(evacUid) - pos).Length() <= evac.Range)
                return true;
        }

        return false;
    }

    private void Announce(string text)
    {
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Служба безопасности Сената", text, SWSenateAnnouncementKind.Security));
    }
}
