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
