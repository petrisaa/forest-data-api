using ForestApi.Import;
using Npgsql;
using NpgsqlTypes;

namespace ForestApi.Data;

public sealed record ImportCounts(long BatchId, int InsertedCount, int UpdatedCount, int RejectedCount);

/// <summary>Stores an import batch and its stands in PostgreSQL in a single transaction.</summary>
public sealed class StandImportRepository(NpgsqlDataSource dataSource)
{
    private const string InsertBatchSql = """
        INSERT INTO import_batch (file_name)
        VALUES (@file_name)
        RETURNING id
        """;

    // xmax = 0 only for a freshly inserted row, so it tells inserts and updates apart.
    private const string UpsertStandSql = """
        INSERT INTO stand (source_id, import_batch_id, stand_number, area_ha, main_tree_species,
                           development_class, volume_m3_per_ha, estimate_date)
        VALUES (@source_id, @import_batch_id, @stand_number, @area_ha, @main_tree_species,
                @development_class, @volume_m3_per_ha, @estimate_date)
        ON CONFLICT (source_id) DO UPDATE
        SET import_batch_id   = EXCLUDED.import_batch_id,
            stand_number      = EXCLUDED.stand_number,
            area_ha           = EXCLUDED.area_ha,
            main_tree_species = EXCLUDED.main_tree_species,
            development_class = EXCLUDED.development_class,
            volume_m3_per_ha  = EXCLUDED.volume_m3_per_ha,
            estimate_date     = EXCLUDED.estimate_date,
            updated_at        = now()
        RETURNING (xmax = 0) AS inserted
        """;

    private const string UpdateBatchCountsSql = """
        UPDATE import_batch
        SET inserted_count = @inserted_count,
            updated_count  = @updated_count,
            rejected_count = @rejected_count
        WHERE id = @id
        """;

    public async Task<ImportCounts> ImportAsync(
        string fileName,
        IReadOnlyList<StandRecord> stands,
        int rejectedCount,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long batchId;
        await using (var insertBatch = new NpgsqlCommand(InsertBatchSql, connection, transaction))
        {
            insertBatch.Parameters.Add(new NpgsqlParameter<string>("file_name", fileName));
            batchId = (long)(await insertBatch.ExecuteScalarAsync(cancellationToken))!;
        }

        int inserted = 0, updated = 0;
        await using (var upsert = new NpgsqlCommand(UpsertStandSql, connection, transaction))
        {
            var sourceId = upsert.Parameters.Add("source_id", NpgsqlDbType.Text);
            upsert.Parameters.Add(new NpgsqlParameter<long>("import_batch_id", batchId));
            var standNumber = upsert.Parameters.Add("stand_number", NpgsqlDbType.Text);
            var areaHa = upsert.Parameters.Add("area_ha", NpgsqlDbType.Numeric);
            var mainTreeSpecies = upsert.Parameters.Add("main_tree_species", NpgsqlDbType.Smallint);
            var developmentClass = upsert.Parameters.Add("development_class", NpgsqlDbType.Text);
            var volume = upsert.Parameters.Add("volume_m3_per_ha", NpgsqlDbType.Numeric);
            var estimateDate = upsert.Parameters.Add("estimate_date", NpgsqlDbType.Date);
            await upsert.PrepareAsync(cancellationToken);

            foreach (var stand in stands)
            {
                sourceId.Value = stand.SourceId;
                standNumber.Value = stand.StandNumber;
                areaHa.Value = stand.AreaHa;
                mainTreeSpecies.Value = (object?)stand.MainTreeSpecies ?? DBNull.Value;
                developmentClass.Value = (object?)stand.DevelopmentClass ?? DBNull.Value;
                volume.Value = (object?)stand.VolumeM3PerHa ?? DBNull.Value;
                estimateDate.Value = (object?)stand.EstimateDate ?? DBNull.Value;

                var wasInserted = (bool)(await upsert.ExecuteScalarAsync(cancellationToken))!;
                if (wasInserted) inserted++; else updated++;
            }
        }

        await using (var updateBatch = new NpgsqlCommand(UpdateBatchCountsSql, connection, transaction))
        {
            updateBatch.Parameters.Add(new NpgsqlParameter<int>("inserted_count", inserted));
            updateBatch.Parameters.Add(new NpgsqlParameter<int>("updated_count", updated));
            updateBatch.Parameters.Add(new NpgsqlParameter<int>("rejected_count", rejectedCount));
            updateBatch.Parameters.Add(new NpgsqlParameter<long>("id", batchId));
            await updateBatch.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImportCounts(batchId, inserted, updated, rejectedCount);
    }
}
