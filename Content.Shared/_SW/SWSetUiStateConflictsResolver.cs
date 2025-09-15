using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

public sealed class SWSetUiStateConflictsResolver : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    private static List<SWUIStateUpdateRequest> _requests = new();
    public void RequestUiUpdate(EntityUid entity, Enum key, BoundUserInterfaceState state)
    {
        _requests.Add(new SWUIStateUpdateRequest(entity, key, state));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_requests.Count != 0)
        {
            var r = _requests[0];
            _uiSystem.SetUiState(r.Entity, r.Key, r.State);
            _requests.RemoveAt(0);
        }
    }
}
public struct SWUIStateUpdateRequest
{
    public BoundUserInterfaceState State;
    public Enum Key;
    public EntityUid Entity;
    public SWUIStateUpdateRequest(EntityUid entity, Enum key, BoundUserInterfaceState state)
    {
        State = state;
        Key = key;
        Entity = entity;
    }
}
