using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ForestApi.Import;

/// <summary>
/// Reads st:Stand elements from Finnish Forest Centre MV1.9 XML (Tapio ForestData V20)
/// and validates them.
/// </summary>
public static class StandXmlParser
{
    private static readonly XNamespace Fd = "http://standardit.tapio.fi/schemas/forestData";
    private static readonly XNamespace St = "http://standardit.tapio.fi/schemas/forestData/Stand";
    private static readonly XNamespace Ts = "http://standardit.tapio.fi/schemas/forestData/treeStand";
    private static readonly XNamespace Tss = "http://standardit.tapio.fi/schemas/forestData/treeStandSummary";

    // TreeStandDataDate/@type: 1 = inventory, 2 = current state, 3 = forecast.
    private const string CurrentStateType = "2";

    private const int MinTreeSpecies = 1;
    private const int MaxTreeSpecies = 30;

    // Upper bounds of the numeric(8,2) and numeric(8,1) columns in db/schema.sql.
    private const decimal MaxAreaHa = 999_999.99m;
    private const decimal MaxVolume = 9_999_999.9m;

    // '.' as decimal separator, no thousands separators: "1,5" must not parse as 15.
    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>Loads XML with DTD processing disabled to prevent XXE and entity expansion attacks.</summary>
    /// <exception cref="XmlException">The content is not well-formed XML.</exception>
    public static XDocument Load(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    /// <summary>Returns true if the document root is a ForestData ForestPropertyData element.</summary>
    public static bool IsForestPropertyData(XDocument document) =>
        document.Root?.Name == Fd + "ForestPropertyData";

    public static ParseResult Parse(XDocument document)
    {
        var accepted = new List<StandRecord>();
        var rejected = new List<RejectedStand>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stand in document.Descendants(St + "Stand"))
        {
            var sourceId = NullIfEmpty(stand.Attribute("id")?.Value);
            var errors = new List<string>();
            var record = ParseStand(stand, sourceId, errors);

            if (sourceId is not null && !seenIds.Add(sourceId))
            {
                errors.Add("duplicate source_id in file");
            }

            if (errors.Count > 0 || record is null)
            {
                rejected.Add(new RejectedStand(sourceId, string.Join("; ", errors)));
            }
            else
            {
                accepted.Add(record);
            }
        }

        return new ParseResult(accepted, rejected);
    }

    private static StandRecord? ParseStand(XElement stand, string? sourceId, List<string> errors)
    {
        if (sourceId is null)
        {
            errors.Add("missing st:Stand/@id");
        }

        var basic = stand.Element(St + "StandBasicData");

        var standNumber = NullIfEmpty(basic?.Element(St + "StandNumber")?.Value);
        if (standNumber is null)
        {
            errors.Add("missing stand_number");
        }

        decimal areaHa = 0;
        var areaText = NullIfEmpty(basic?.Element(St + "Area")?.Value);
        if (areaText is null)
        {
            errors.Add("missing area_ha");
        }
        else if (!TryParseDecimal(areaText, out areaHa))
        {
            errors.Add($"area_ha is not a number: '{areaText}'");
        }
        else if (areaHa <= 0)
        {
            errors.Add($"area_ha must be greater than 0 (was {areaText})");
        }
        else if (areaHa > MaxAreaHa)
        {
            errors.Add($"area_ha is too large (was {areaText})");
        }

        short? mainTreeSpecies = null;
        var speciesText = NullIfEmpty(basic?.Element(St + "MainTreeSpecies")?.Value);
        if (speciesText is not null)
        {
            if (short.TryParse(speciesText, NumberStyles.None, CultureInfo.InvariantCulture, out var species)
                && species is >= MinTreeSpecies and <= MaxTreeSpecies)
            {
                mainTreeSpecies = species;
            }
            else
            {
                errors.Add($"main_tree_species must be between {MinTreeSpecies} and {MaxTreeSpecies} (was {speciesText})");
            }
        }

        var developmentClass = NullIfEmpty(basic?.Element(St + "DevelopmentClass")?.Value);

        decimal? volume = null;
        DateOnly? estimateDate = null;
        var current = stand
            .Elements(Ts + "TreeStandData")
            .Elements(Ts + "TreeStandDataDate")
            .Where(e => (string?)e.Attribute("type") == CurrentStateType)
            .ToList();

        if (current.Count > 1)
        {
            errors.Add("multiple current state (type 2) TreeStandDataDate elements");
        }
        else if (current.Count == 1)
        {
            var dateText = NullIfEmpty(current[0].Attribute("date")?.Value);
            if (dateText is not null)
            {
                if (DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    estimateDate = date;
                }
                else
                {
                    errors.Add($"current state date is not a valid date: '{dateText}'");
                }
            }

            var volumeText = NullIfEmpty(current[0].Element(Tss + "TreeStandSummary")?.Element(Tss + "Volume")?.Value);
            if (volumeText is not null)
            {
                if (!TryParseDecimal(volumeText, out var v))
                {
                    errors.Add($"volume is not a number: '{volumeText}'");
                }
                else if (v < 0)
                {
                    errors.Add($"volume must not be negative (was {volumeText})");
                }
                else if (v > MaxVolume)
                {
                    errors.Add($"volume is too large (was {volumeText})");
                }
                else
                {
                    volume = v;
                }
            }
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new StandRecord(sourceId!, standNumber!, areaHa, mainTreeSpecies, developmentClass, volume, estimateDate);
    }

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, DecimalStyle, CultureInfo.InvariantCulture, out value);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
