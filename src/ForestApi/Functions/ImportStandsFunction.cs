using System.Xml;
using ForestApi.Data;
using ForestApi.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ForestApi.Functions;

public sealed class ImportStandsFunction(StandImportRepository repository, ILogger<ImportStandsFunction> logger)
{
    private const long MaxBodyBytes = 5 * 1024 * 1024;
    private const int MaxFileNameLength = 255;
    private const string DefaultFileName = "request-body.xml";

    [Function("ImportStands")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stands/import")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return TooLarge();
        }

        var fileName = request.Query["fileName"].ToString().Trim();
        if (fileName.Length == 0)
        {
            fileName = DefaultFileName;
        }
        else if (fileName.Length > MaxFileNameLength)
        {
            return Problem(StatusCodes.Status400BadRequest, $"fileName must be at most {MaxFileNameLength} characters.");
        }

        // Content-Length may be missing (chunked transfer), so enforce the limit while reading.
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > MaxBodyBytes)
            {
                return TooLarge();
            }
            body.Write(buffer, 0, read);
        }

        if (body.Length == 0)
        {
            return Problem(StatusCodes.Status400BadRequest, "Request body is empty. Send the XML file as the request body.");
        }

        body.Position = 0;
        ParseResult parsed;
        try
        {
            var document = StandXmlParser.Load(body);
            if (!StandXmlParser.IsForestPropertyData(document))
            {
                return Problem(StatusCodes.Status400BadRequest, "Root element must be ForestPropertyData in the ForestData namespace.");
            }
            parsed = StandXmlParser.Parse(document);
        }
        catch (XmlException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, $"Invalid XML: {ex.Message}");
        }

        ImportCounts counts;
        try
        {
            counts = await repository.ImportAsync(fileName, parsed.Accepted, parsed.Rejected.Count, cancellationToken);
        }
        catch (Npgsql.NpgsqlException ex)
        {
            logger.LogError(ex, "Importing {FileName} failed, transaction rolled back", fileName);
            return Problem(StatusCodes.Status500InternalServerError, "Saving the stands to the database failed. No changes were made.");
        }

        logger.LogInformation(
            "Imported {FileName} as batch {BatchId}: {Inserted} inserted, {Updated} updated, {Rejected} rejected",
            fileName, counts.BatchId, counts.InsertedCount, counts.UpdatedCount, counts.RejectedCount);

        return new OkObjectResult(new ImportResponse(
            counts.BatchId,
            fileName,
            counts.InsertedCount,
            counts.UpdatedCount,
            counts.RejectedCount,
            parsed.Rejected));
    }

    private static ObjectResult TooLarge() =>
        Problem(StatusCodes.Status413PayloadTooLarge, $"Request body must be at most {MaxBodyBytes / (1024 * 1024)} MB.");

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Detail = detail }) { StatusCode = status };

    private sealed record ImportResponse(
        long BatchId,
        string FileName,
        int InsertedCount,
        int UpdatedCount,
        int RejectedCount,
        IReadOnlyList<RejectedStand> Rejected);
}
