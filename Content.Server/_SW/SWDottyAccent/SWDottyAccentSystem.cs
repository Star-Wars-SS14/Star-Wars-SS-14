using Content.Shared.Speech;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.SWDottyAccent;

public sealed partial class SWDottyAccentSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDottyAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    private void OnAccentGet(Entity<SWDottyAccentComponent> entity, ref AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message);
    }

    public static string Accentuate(string message)
    {
        // Разделяем строку на слова и добавляем между ними многоточия
        string[] words = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("... ", words) + "...";
    }
}
