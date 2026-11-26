using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    /// <summary>
    /// Evaluates virtual trade without a fictitious actor or the FTL graph. D retains blocked cargo and its provenance.
    /// This gate neither moves goods nor pays tax; D commits those operations only after all its own checks pass.
    /// </summary>
    public SWGlobalWarTradeDecision EvaluateTrade(SWGlobalWarTradeRequest request)
    {
        var denied = new SWGlobalWarTradeDecision(false, "sw-global-war-trade-unavailable", 0, 0, 0, 0, 0);
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != request.Generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !Enum.IsDefined(request.Stage) || !Enum.IsDefined(request.Position) || request.Quantity <= 0 ||
            !Enum.IsDefined(request.Origin) || !Enum.IsDefined(request.CargoOwner) ||
            !_prototypes.HasIndex(request.Commodity) || !_planets.TryGetPlanet(request.Sender, out var sender) ||
            !_planets.TryGetPlanet(request.Recipient, out var recipient) ||
            !_planets.TryGetPlanet(request.Current, out var current) || !_planets.TryGetPlanet(request.Next, out var next))
            return denied;
        var source = Comp<SWGlobalWarPlanetComponent>(sender.Value);
        var destination = Comp<SWGlobalWarPlanetComponent>(recipient.Value);
        var from = Comp<SWGlobalWarPlanetComponent>(current.Value);
        var to = Comp<SWGlobalWarPlanetComponent>(next.Value);
        denied = denied with
        {
            PolicyRevision = politics.Comp.Revision,
            SenderRevision = source.OwnershipRevision,
            RecipientRevision = destination.OwnershipRevision,
            CurrentRevision = from.OwnershipRevision,
            NextRevision = to.OwnershipRevision,
        };
        if (request.Stage == SWGlobalWarTradeStage.Dispatch &&
            (request.Position != SWGlobalWarTradePosition.AtNode || request.Current != request.Sender) ||
            request.Stage == SWGlobalWarTradeStage.Receipt && request.Next != request.Recipient)
            return denied;
        if (request.Position == SWGlobalWarTradePosition.AtNode && OrbitBlocksTrade(politics.Comp, from) ||
            OrbitBlocksTrade(politics.Comp, to))
            return denied with { Reason = "sw-global-war-trade-blockade" };
        var capturedForDomesticUse = HasDomesticCaptureEvidence(request, destination, to);
        var origin = capturedForDomesticUse ? request.CargoOwner : request.Origin;
        var origins = new[] { origin, request.CargoOwner,
            request.Position == SWGlobalWarTradePosition.AtNode ? from.SurfaceOwner : SWGlobalWarFaction.Neutral };
        var receivers = new[] { destination.SurfaceOwner, to.SurfaceOwner };
        foreach (var exporter in origins)
        {
            foreach (var receiver in receivers)
            {
                if (exporter == SWGlobalWarFaction.Neutral || receiver == SWGlobalWarFaction.Neutral || exporter == receiver)
                    continue;
                if (politics.Comp.Relations == SWGlobalWarRelations.War)
                    return denied with { Reason = "sw-global-war-trade-war" };
                if (politics.Comp.Sanctions.Contains((exporter, request.Commodity)))
                    return denied with { Reason = "sw-global-war-trade-sanction" };
            }
        }
        return denied with { Allowed = true, Reason = string.Empty };
    }

    private bool HasDomesticCaptureEvidence(SWGlobalWarTradeRequest request,
        SWGlobalWarPlanetComponent recipient, SWGlobalWarPlanetComponent next)
    {
        return request.Capture is { } proof && proof.Generation == request.Generation &&
               proof.CaptureOperation != Guid.Empty && proof.TransferOperation != Guid.Empty && request.Batch != Guid.Empty &&
               proof.Batch == request.Batch && request.Quantity <= proof.Quantity && proof.PreviousOwner != proof.NewOwner &&
               Enum.IsDefined(proof.PreviousOwner) && proof.NewOwner is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire &&
               request.CargoOwner == proof.NewOwner && recipient.SurfaceOwner == proof.NewOwner &&
               next.SurfaceOwner == proof.NewOwner && _planets.TryGetPlanet(proof.Node, out var captured) &&
               _planets.HasOwnershipReceipt(captured.Value, request.Generation, proof.CaptureOperation,
                   SWGlobalWarPlanetLocation.Surface, proof.NewOwner);
    }

    private static bool OrbitBlocksTrade(SWGlobalWarPoliticsComponent state, SWGlobalWarPlanetComponent planet)
    {
        // A neutral claimant's initial orbit is not a blockade of its own unresolved political accession.
        if (state.Neutrals.TryGetValue(planet.Prototype, out var neutral) &&
            neutral.Phase != SWGlobalWarNeutralPhase.Integrated)
            return false;
        return planet.OrbitOwner != planet.SurfaceOwner;
    }
}
