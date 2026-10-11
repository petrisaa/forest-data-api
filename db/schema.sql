-- PostgreSQL schema for forest stand data imported from Finnish Forest Centre
-- MV1.9 XML (Tapio ForestData schema package V20).

-- One row per imported XML file.
CREATE TABLE import_batch (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    file_name       text        NOT NULL,
    imported_at     timestamptz NOT NULL DEFAULT now(),
    inserted_count  integer     NOT NULL DEFAULT 0,
    updated_count   integer     NOT NULL DEFAULT 0,
    rejected_count  integer     NOT NULL DEFAULT 0
);

-- One row per forest stand (st:Stand). Repeated imports are upserted on source_id:
--
--   INSERT INTO stand (source_id, import_batch_id, stand_number, ...)
--   VALUES (...)
--   ON CONFLICT (source_id) DO UPDATE
--   SET import_batch_id = EXCLUDED.import_batch_id,
--       stand_number    = EXCLUDED.stand_number,
--       ...,
--       updated_at      = now()          -- the column default applies to INSERT only
--   RETURNING (xmax = 0) AS inserted;    -- true = inserted, false = updated
CREATE TABLE stand (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- st:Stand/@id. Not guaranteed stable across exports; a split or merged stand
    -- probably gets a new id.
    source_id           text         NOT NULL UNIQUE,
    -- Batch that last inserted or updated the row.
    import_batch_id     bigint       NOT NULL REFERENCES import_batch (id),
    -- st:StandNumber. Not unique within an export.
    stand_number        text         NOT NULL,
    -- st:Area
    area_ha             numeric(8,2) NOT NULL CHECK (area_ha > 0),
    -- st:MainTreeSpecies (code list value)
    main_tree_species   smallint     NULL,
    -- st:DevelopmentClass, e.g. '02' or 'T2'
    development_class   text         NULL,
    -- tss:Volume of the current (type 2) tree stand data
    volume_m3_per_ha    numeric(8,1) NULL CHECK (volume_m3_per_ha >= 0),
    -- ts:TreeStandDataDate/@date of the current (type 2) tree stand data
    estimate_date       date         NULL,
    updated_at          timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX stand_import_batch_id_idx ON stand (import_batch_id);
