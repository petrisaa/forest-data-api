# forest-data-api
C# Azure Functions API for ingesting forest stand XML data into PostgreSQL

## Data

### Source

The sample data comes from the Finnish Forest Centre (Suomen metsäkeskus) open forest
resource data: forest stands (metsävarakuviot) for map sheet **K3243H**, in the
**MV1.9 XML format** (Tapio ForestData schema package V20). The file was exported on
2026-10-09.

The Finnish Forest Centre has announced that it will discontinue distributing forest
resource data in XML format.

### License

The data is licensed under [Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/).
Attribution: *Contains forest resource data from the Finnish Forest Centre, 2026.*

### Files

| File | Contents | In repository |
|---|---|---|
| `data/raw/XML_MV_K3243H.xml` | Full original export (51 stands) | No, ignored via `.gitignore` |
| `data/sample-stands.xml` | Sample for development and tests | Yes |

`data/sample-stands.xml` contains:

- the first **20 stands** from the original file, unchanged (including namespaces and geometries)
- **2 deliberately invalid test stands**, marked with XML comments, for testing validation:
  - stand number `9901`: negative area (`st:Area` = `-1.50`)
  - stand number `9902`: unknown tree species code `99` (`st:MainTreeSpecies` and `tss:MainTreeSpecies`)

The raw file is not stored in the repository. To recreate it, download the open forest
stand data for map sheet K3243H from the Finnish Forest Centre and place it in `data/raw/`.

### Privacy

The files contain public open data only: stand geometries and forest attributes. They
contain no property identifiers, owner information or other personal data.

## Database

Local development uses **PostgreSQL 17**, run in Docker (the dev container includes
Docker-in-Docker). The schema is in `db/schema.sql`.

```bash
docker run -d --name forest-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:17
docker exec -i forest-db psql -U postgres -v ON_ERROR_STOP=1 < db/schema.sql
```

Repeated imports are upserted on `stand.source_id`, which holds the `st:Stand/@id`
attribute from the XML.

## API

The Azure Functions project (.NET 10, isolated worker) is in `src/ForestApi`.

### Configuration

The database connection string is read from the `ConnectionStrings__ForestDb` setting.
For local development, copy the example settings file and set the password:

```bash
cp src/ForestApi/local.settings.example.json src/ForestApi/local.settings.json
```

`local.settings.json` is ignored by git and must never be committed. In Azure, set
`ConnectionStrings__ForestDb` as an application setting.

### Running locally

```bash
cd src/ForestApi
func start
```

### POST /api/stands/import

Imports an MV1.9 XML file sent as the raw request body (max 5 MB). The optional
`fileName` query parameter is stored in `import_batch.file_name`.

```bash
curl -X POST -H "Content-Type: application/xml" \
  --data-binary @data/sample-stands.xml \
  "http://localhost:7071/api/stands/import?fileName=sample-stands.xml"
```

For each `st:Stand` the function reads `@id` (`source_id`), `st:StandNumber`, `st:Area`,
`st:MainTreeSpecies`, `st:DevelopmentClass`, and the volume and date of the current state
tree stand data (`ts:TreeStandDataDate type="2"`). A stand is rejected if:

- `area_ha` is missing, not a number, or not greater than 0
- `main_tree_species` is present but not between 1 and 30
- the volume is negative
- `@id` or `st:StandNumber` is missing, `@id` is repeated in the file, or a value does not
  fit the database column

Accepted stands are upserted on `source_id` in a single transaction together with an
`import_batch` row. Example response:

```json
{
  "batchId": 1,
  "fileName": "sample-stands.xml",
  "insertedCount": 20,
  "updatedCount": 0,
  "rejectedCount": 2,
  "rejected": [
    { "sourceId": "9929729766", "reason": "area_ha must be greater than 0 (was -1.50)" },
    { "sourceId": "9829729766", "reason": "main_tree_species must be between 1 and 30 (was 99)" }
  ]
}
```

Importing `data/sample-stands.xml` twice against an empty database gives these results.
The second import updates the existing rows instead of creating duplicates:

| Import | `insertedCount` | `updatedCount` | `rejectedCount` |
|---|---|---|---|
| First | 20 | 0 | 2 |
| Second | 0 | 20 | 2 |

| Status | Meaning |
|---|---|
| 200 | Imported; rejected stands are listed in the response |
| 400 | Empty body, malformed XML, DTD present, or root is not `ForestPropertyData` |
| 413 | Body larger than 5 MB |
| 500 | Database error; the transaction was rolled back |

### GET /api/stands/summary

Returns stand totals grouped by main tree species, calculated in the database with a single
`GROUP BY` query:

- `standCount`: number of stands
- `totalAreaHa`: sum of `area_ha`
- `totalVolumeM3`: sum of `area_ha × volume_m3_per_ha`, rounded to 0.1 m³
- `volumeMissingCount`: stands without a volume; they are not included in `totalVolumeM3`

Stands without a main tree species are grouped together with `mainTreeSpecies: null`, listed
last. `speciesName` is set for known codes (1 = pine, 2 = spruce, 29 = broadleaf) and is
`null` for other codes.

```bash
curl "http://localhost:7071/api/stands/summary"
```

Example response after importing `data/sample-stands.xml`:

```json
{
  "species": [
    { "mainTreeSpecies": 1, "speciesName": "pine", "standCount": 9, "totalAreaHa": 5.48, "totalVolumeM3": 359.8, "volumeMissingCount": 0 },
    { "mainTreeSpecies": 2, "speciesName": "spruce", "standCount": 5, "totalAreaHa": 5.10, "totalVolumeM3": 23.4, "volumeMissingCount": 0 },
    { "mainTreeSpecies": 29, "speciesName": "broadleaf", "standCount": 3, "totalAreaHa": 3.55, "totalVolumeM3": 57.4, "volumeMissingCount": 0 },
    { "mainTreeSpecies": null, "speciesName": null, "standCount": 3, "totalAreaHa": 2.52, "totalVolumeM3": 0.0, "volumeMissingCount": 0 }
  ]
}
```

| Status | Meaning |
|---|---|
| 200 | Summary returned; `species` is empty if there are no stands |
| 500 | Database error |

## Known limitations

- **Split or merged stands leave stale rows.** `st:Stand/@id` appears to be a stable
  database key in the Finnish Forest Centre system, but a stand that is split or merged
  most likely gets a new id. The upsert then inserts the new stand, and the row for the
  old id stays in the database even though it no longer appears in any export.
  A possible solution is to track the last batch that contained each stand (for example a
  `last_seen_batch_id` column) and mark stands missing from a full export of the same map
  sheet as removed (for example with a `deleted_at` timestamp) instead of deleting them.
