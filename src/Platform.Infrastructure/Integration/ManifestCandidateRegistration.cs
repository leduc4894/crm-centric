namespace Platform.Infrastructure.Integration;

internal enum CandidateStage { ManifestFetch, SidecarFetch, Publisher, ManifestValidation, Schemas, Registry }

internal sealed class CandidateRegistrationException(CandidateStage stage, Enum reason, Guid correlationId) : Exception(reason.ToString())
{
    public CandidateStage Stage { get; } = stage;
    public Enum Reason { get; } = reason;
    public Guid CorrelationId { get; } = correlationId;
}

internal sealed class ManifestCandidateRegistration(SafeFetchClient client, ManifestRegistry registry)
{
    public async Task<RegistryResult> RegisterAsync(Uri manifestUrl, FetchPolicy policy, PublisherTrustSnapshot trust,
        Guid correlationId, CancellationToken ct)
    {
        var stage = CandidateStage.ManifestFetch;
        try
        {
            var manifest = await client.FetchAsync(manifestUrl, policy, SafeFetchClient.ManifestLimit, ct);
            stage = CandidateStage.SidecarFetch;
            var sidecar = await client.FetchAsync(SafeFetchClient.SidecarUri(manifestUrl), policy, SafeFetchClient.SidecarLimit, ct);
            stage = CandidateStage.Publisher;
            var publisher = PublisherVerifier.Verify(manifest, sidecar, trust);
            stage = CandidateStage.ManifestValidation;
            var candidate = ManifestValidator.Validate(manifest.Bytes.AsMemory(), publisher);
            stage = CandidateStage.Schemas;
            var schemas = await new SchemaSnapshotLoader(client).LoadAsync(candidate, policy, ct);
            stage = CandidateStage.Registry;
            return await registry.RegisterAsync(candidate, schemas, correlationId, ct);
        }
        catch (ManifestFetchException ex) { throw new CandidateRegistrationException(stage, ex.Reason, correlationId); }
        catch (PublisherVerificationException ex) { throw new CandidateRegistrationException(stage, ex.Reason, correlationId); }
        catch (ManifestValidationException ex) { throw new CandidateRegistrationException(stage, ex.Reason, correlationId); }
        catch (RegistryException ex) { throw new CandidateRegistrationException(stage, ex.Reason, correlationId); }
    }
}
