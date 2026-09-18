using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public readonly partial record struct ProjectBlobs(GuidObjectId ProjectId, BlobUri BlobUri);
