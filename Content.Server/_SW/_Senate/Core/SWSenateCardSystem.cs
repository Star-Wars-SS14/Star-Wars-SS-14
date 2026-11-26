using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

/// <summary>Keep political credentials in step with appointments without replacing personal access.</summary>
public sealed class SWSenateCardSystem : EntitySystem
{
    [Dependency] private readonly SharedAccessSystem _access = default!;
    [Dependency] private readonly SharedIdCardSystem _id = default!;

    private static readonly HashSet<ProtoId<AccessLevelPrototype>> OfficeTags = new()
    {
        "SWSenateChancellor", "SWSenateSpeaker", "SWSenateSenator", "SWMinister", "SWSenateCourt",
    };

    public void Synchronize(EntityUid card, SWSenatePosition position)
    {
        if (!HasComp<IdCardComponent>(card))
            return;

        var credential = EnsureComp<SWSenatePositionCardComponent>(card);
        credential.Position = position;
        if (position != SWSenatePosition.Senator && TryComp<SWSenateSectorComponent>(card, out var sector))
            sector.Sector = null;
        if (position != SWSenatePosition.Minister && TryComp<SWMinistryComponent>(card, out var ministry))
            ministry.Ministry = null;

        SetOfficeAccess(card, position);
        _id.TryChangeJobTitle(card, position switch
        {
            SWSenatePosition.Chancellor => "Верховный Канцлер",
            SWSenatePosition.Speaker => "Спикер Сената",
            SWSenatePosition.Minister => "Министр",
            SWSenatePosition.ChiefJustice => "Верховный Судья",
            SWSenatePosition.Judge => "Республиканский Судья",
            SWSenatePosition.Prosecutor => "Обвинитель Республики",
            SWSenatePosition.Defender => "Защитник Республики",
            _ => "Сенатор",
        });
    }

    public void SynchronizeHolder(string name, SWSenatePosition position)
    {
        var query = EntityQueryEnumerator<IdCardComponent>();
        while (query.MoveNext(out var uid, out var id))
        {
            if (id.FullName == name)
                Synchronize(uid, position);
        }
    }

    public void RevokeHolder(string name, SWSenatePosition position)
    {
        var query = EntityQueryEnumerator<IdCardComponent, SWSenatePositionCardComponent>();
        while (query.MoveNext(out var uid, out var id, out var credential))
        {
            if (id.FullName == name && credential.Position == position)
                SetOfficeAccess(uid, null);
        }
    }

    private void SetOfficeAccess(EntityUid card, SWSenatePosition? position)
    {
        var access = EnsureComp<AccessComponent>(card);
        var tags = access.Tags.Where(tag => !OfficeTags.Contains(tag)).ToHashSet();
        tags.Add("SWSenate");
        if (position != null)
        {
            tags.Add(position switch
            {
                SWSenatePosition.Chancellor => "SWSenateChancellor",
                SWSenatePosition.Speaker => "SWSenateSpeaker",
                SWSenatePosition.Senator => "SWSenateSenator",
                SWSenatePosition.Minister => "SWMinister",
                _ => "SWSenateCourt",
            });
        }

        if (!access.Tags.SetEquals(tags))
            _access.TrySetTags(card, tags, access);
    }
}
