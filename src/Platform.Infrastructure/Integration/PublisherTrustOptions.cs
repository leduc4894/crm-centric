using System.Collections.Immutable;

namespace Platform.Infrastructure.Integration;

internal enum PublisherKeyStatus { Unknown, Active, Revoked }

internal sealed record PublisherTrustEntry(string ModuleId, string KeyId, ImmutableArray<byte> PublicKey,
    ImmutableArray<Uri> AllowedOrigins, PublisherKeyStatus Status);

internal sealed record PublisherTrustSnapshot(long Revision, ImmutableArray<PublisherTrustEntry> Entries);
