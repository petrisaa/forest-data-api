using ForestApi.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ForestApi.Functions;

public sealed class StandSummaryFunction(StandSummaryRepository repository, ILogger<StandSummaryFunction> logger)
{
    // st:MainTreeSpecies code list values with a known name; other codes are returned without one.
    private static readonly Dictionary<short, string> SpeciesNames = new()
    {
        [1] = "pine",
        [2] = "spruce",
        [29] = "broadleaf",
    };

    [Function("StandSummary")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "stands/summary")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SpeciesSummary> summaries;
        try
        {
            summaries = await repository.GetSummaryAsync(cancellationToken);
        }
        catch (Npgsql.NpgsqlException ex)
        {
            logger.LogError(ex, "Reading the stand summary failed");
            return Problem(StatusCodes.Status500InternalServerError, "Reading the stand summary from the database failed.");
        }

        var species = summaries
            .Select(s => new SpeciesSummaryResponse(
                s.MainTreeSpecies,
                s.MainTreeSpecies is { } code ? SpeciesNames.GetValueOrDefault(code) : null,
                s.StandCount,
                s.TotalAreaHa,
                s.TotalVolumeM3,
                s.VolumeMissingCount))
            .ToList();

        return new OkObjectResult(new SummaryResponse(species));
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Detail = detail }) { StatusCode = status };

    private sealed record SummaryResponse(IReadOnlyList<SpeciesSummaryResponse> Species);

    private sealed record SpeciesSummaryResponse(
        short? MainTreeSpecies,
        string? SpeciesName,
        int StandCount,
        decimal TotalAreaHa,
        decimal TotalVolumeM3,
        int VolumeMissingCount);
}
