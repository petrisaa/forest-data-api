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

## Known limitations

- **Split or merged stands leave stale rows.** `st:Stand/@id` appears to be a stable
  database key in the Finnish Forest Centre system, but a stand that is split or merged
  most likely gets a new id. The upsert then inserts the new stand, and the row for the
  old id stays in the database even though it no longer appears in any export.
  A possible solution is to track the last batch that contained each stand (for example a
  `last_seen_batch_id` column) and mark stands missing from a full export of the same map
  sheet as removed (for example with a `deleted_at` timestamp) instead of deleting them.
