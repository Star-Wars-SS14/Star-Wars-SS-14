using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._SW._Senate.Economy;

[RegisterComponent]
public sealed partial class SWStaffShiftConsoleComponent : Component
{
}

// Staff roles have no take-office console anywhere else, so unlike every political/executive/judicial
// post there is no existing "who holds this right now" tracking to pay against. This console IS that
// tracking: clocking in reads the actor's own current Job off their Mind (self-service, one active
// role per character name) and doubles as both the on-shift roster and the salary ledger.
public sealed class SWSenateStaffSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    private static readonly Dictionary<string, string> StaffRoles = new()
    {
        ["SWUsher"] = "Пристав Сената",
        ["SWSenateGuardHead"] = "Глава Сенатской Гвардии",
        ["SWSenateGuardOfficer"] = "Офицер Сенатской Гвардии",
        ["SWSenateDoctor"] = "Сенатский врач",
    };

    private static readonly Dictionary<string, int> StaffPay = new()
    {
        ["SWUsher"] = 40,
        ["SWSenateGuardHead"] = 60,
        ["SWSenateGuardOfficer"] = 45,
        ["SWSenateDoctor"] = 55,
    };

    // Ghost-role dummies have no Job on their mind, so the worn/held ID card's stamped job icon is
    // the fallback identity - PresetIdCard writes a unique icon per staff role.
    private static readonly Dictionary<string, string> IconToRole = new()
    {
        ["JobIconUsher"] = "SWUsher",
        ["JobIconSenateGuardHead"] = "SWSenateGuardHead",
        ["JobIconSenateGuardOfficer"] = "SWSenateGuardOfficer",
        ["JobIconSenateDoctor"] = "SWSenateDoctor",
    };

    private readonly Dictionary<string, string> _onShift = new();
    public IEnumerable<string> OnShiftNames => _onShift.Keys;

    private int _lastPaidDay = -1;
    private int _lastPaidHour = -1;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<SWStaffShiftConsoleComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<SWStaffShiftConsoleComponent, SWStaffShiftToggleMessage>(OnToggle);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _onShift.Clear();
        _lastPaidDay = -1;
        _lastPaidHour = -1;
    }

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (_time.Day == _lastPaidDay && _time.CurrentHour == _lastPaidHour)
            return;

        _lastPaidDay = _time.Day;
        _lastPaidHour = _time.CurrentHour;

        foreach (var (name, roleId) in _onShift)
        {
            if (StaffPay.TryGetValue(roleId, out var pay))
                _corruption.AddAccount(name, pay);
        }
    }

    private void OnOpen(Entity<SWStaffShiftConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUi(ent, args.User, string.Empty);
    }

    private void OnToggle(Entity<SWStaffShiftConsoleComponent> ent, ref SWStaffShiftToggleMessage args)
    {
        var actor = args.Actor;

        if (!TryGetStaffRole(actor, out var roleId, out _))
        {
            UpdateUi(ent, actor, "Должность не опознана: нужна ID-карта персонала Сената при себе.");
            return;
        }

        var name = Name(actor);
        var notice = string.Empty;

        if (_onShift.ContainsKey(name))
        {
            _onShift.Remove(name);
            notice = "Смена сдана.";
        }
        else
        {
            _onShift[name] = roleId;
            notice = "Смена принята.";
        }

        UpdateUi(ent, actor, notice);
    }

    private bool TryGetStaffRole(EntityUid actor, out string roleId, out string roleName)
    {
        roleId = string.Empty;
        roleName = string.Empty;

        if (_mind.TryGetMind(actor, out var mindId, out _)
            && _jobs.MindTryGetJob(mindId, out var job)
            && StaffRoles.ContainsKey(job.ID))
        {
            roleId = job.ID;
            roleName = StaffRoles[job.ID];
            return true;
        }

        if (_idCard.TryFindIdCard(actor, out var card)
            && IconToRole.TryGetValue(card.Comp.JobIcon.Id, out var cardRole))
        {
            roleId = cardRole;
            roleName = StaffRoles[cardRole];
            return true;
        }

        return false;
    }

    private void UpdateUi(EntityUid uid, EntityUid actor, string notice)
    {
        var name = Name(actor);
        TryGetStaffRole(actor, out var myRoleId, out var myRoleName);

        var state = new SWStaffShiftConsoleState
        {
            UserName = name,
            MyRoleId = myRoleId.Length > 0 ? myRoleId : null,
            MyRoleName = myRoleName,
            OnShift = _onShift.ContainsKey(name),
            Notice = notice,
        };

        foreach (var (staffName, roleId) in _onShift.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            state.Roster.Add(new SWStaffShiftEntry
            {
                RoleId = roleId,
                RoleName = StaffRoles.TryGetValue(roleId, out var roleName) ? roleName : roleId,
                Name = staffName,
            });
        }

        _ui.SetUiState(uid, SWStaffShiftConsoleUiKey.Key, state);
    }
}
