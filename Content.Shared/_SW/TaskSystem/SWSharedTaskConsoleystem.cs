using Content.Shared.Access.Systems;
using Content.Shared.UserInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[Virtual]
public class SWSharedTaskConsoleystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TaskConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpen);
    }

    protected void OnOpen(Entity<TaskConsoleComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!_accessReaderSystem.IsAllowed(args.User, ent))
        {
            args.Cancel();
        }
    }
}
