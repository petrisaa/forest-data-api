using Npgsql;

namespace ForestApi.Data;

/// <summary>Totals for the stands that share a main tree species. MainTreeSpecies is null for stands without one.</summary>
public sealed record SpeciesSummary(
    short? MainTreeSpecies,
    int StandCount,
    decimal TotalAreaHa,
    decimal TotalVolumeM3,
    int VolumeMissingCount);

/// <summary>Calculates stand totals per main tree species in PostgreSQL.</summary>
public sealed class StandSummaryRepository(NpgsqlDataSource dataSource)
{
    // SUM skips stands without a volume, so they are counted separately.
    private const string SummarySql = """
        SELECT main_tree_species,
               count(*)::integer                                         AS stand_count,
               sum(area_ha)                                              AS total_area_ha,
               round(coalesce(sum(area_ha * volume_m3_per_ha), 0), 1)    AS total_volume_m3,
               count(*) FILTER (WHERE volume_m3_per_ha IS NULL)::integer AS volume_missing_count
        FROM stand
        GROUP BY main_tree_species
        ORDER BY main_tree_species NULLS LAST
        """;

    public async Task<IReadOnlyList<SpeciesSummary>> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SummarySql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var summaries = new List<SpeciesSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new SpeciesSummary(
                reader.IsDBNull(0) ? null : reader.GetInt16(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetInt32(4)));
        }
        return summaries;
    }
}
