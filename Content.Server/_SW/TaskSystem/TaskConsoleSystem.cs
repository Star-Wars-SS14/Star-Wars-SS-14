using Content.Server._SW.TaskSystem.SWAddMoneyOnCompleted;
using Content.Server.Communications;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Communications;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;
public sealed partial class TaskConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    /// <summary>
    /// Task progress has no change event: most tasks compute it on request, so open consoles poll it.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TaskConsoleComponent, BoundUIOpenedEvent>(OnOpened);
    }

    private void OnOpened(Entity<TaskConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateTaskConsoleInterface(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
        {
            return;
        }

        _nextRefresh = _timing.CurTime + RefreshInterval;

        var query = EntityQueryEnumerator<TaskConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_uiSystem.IsUiOpen(uid, TaskConsoleUiKey.Key))
            {
                UpdateTaskConsoleInterface(uid, comp);
            }
        }
    }

    private void UpdateTaskConsoleInterface(EntityUid uid, TaskConsoleComponent comp)
    {
        List<Entity<SWTaskComponent>> list = _sWTasks.GetFactionTasks(comp.Faction);
        List<TaskInfo> listComp = new();
        foreach(var task in list)
        {
            // A task aimed at a person is worth what the target's job is worth, not its base score.
            var score = _sWTasks.GetTaskScore(task);
            var scoreText = score > 0 ? Loc.GetString("sw-task-console-score", ("score", score)) : string.Empty;
            listComp.Add(new TaskInfo(task.Comp.Name, task.Comp.Description, task.Comp.Completeness, GetRewards(task, score), scoreText));
        }
        var state = new TaskConsoleBounderUserInterfaceState(listComp);

        _uiSystem.SetUiState(uid, TaskConsoleUiKey.Key, state);
    }

    private List<string> GetRewards(Entity<SWTaskComponent> task, int score)
    {
        var rewards = new List<string>();

        if (TryComp<SWAddMoneyOnCompletedComponent>(task, out var money))
            AddMoneyRewards(rewards, money.Amount, 1);

        if (TryComp<SWAddMoneyBasedOnTargetScoreComponent>(task, out var perScore))
            AddMoneyRewards(rewards, perScore.MoneyPerScore, score);

        return rewards;
    }

    private void AddMoneyRewards(List<string> rewards, Dictionary<ProtoId<SWCurrencyPrototype>, float> money, int times)
    {
        foreach (var (currency, amount) in money)
        {
            var name = Loc.GetString(_prototype.Index(currency).ShortName);
            rewards.Add(Loc.GetString("sw-task-console-reward", ("currency", name), ("amount", amount * times)));
        }
    }
}
