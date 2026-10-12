namespace ForestApi.Import;

/// <summary>A validated forest stand, ready to be upserted into the stand table.</summary>
public sealed record StandRecord(
    string SourceId,
    string StandNumber,
    decimal AreaHa,
    short? MainTreeSpecies,
    string? DevelopmentClass,
    decimal? VolumeM3PerHa,
    DateOnly? EstimateDate);

/// <summary>A stand that failed validation and is not stored.</summary>
public sealed record RejectedStand(string? SourceId, string Reason);

public sealed record ParseResult(IReadOnlyList<StandRecord> Accepted, IReadOnlyList<RejectedStand> Rejected);
