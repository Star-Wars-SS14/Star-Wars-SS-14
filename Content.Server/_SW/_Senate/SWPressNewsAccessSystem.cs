using Content.Server._SW._Senate.Press;
using Content.Shared.Verbs;

namespace Content.Server._SW._Senate;

[RegisterComponent]
public sealed partial class SWPressNewsAccessComponent : Component { }

public sealed class SWPressNewsAccessSystem : EntitySystem
{
    [Dependency] private readonly SWPressTerminalSystem _press = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWPressNewsAccessComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<SWPressNewsAccessComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, SWPressNewsAccessComponent component, ComponentShutdown args)
        => _press.CloseFor(uid);

    private void OnVerbs(EntityUid uid, SWPressNewsAccessComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Новости Голонета",
            Act = () => _press.OpenPublic(uid, user),
        });
    }
}
