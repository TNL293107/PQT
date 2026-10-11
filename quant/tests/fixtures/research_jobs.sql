-- research.jobs as the backend's migration creates it (ResearchBoundary).
--
-- The backend owns this table (ADR-016); the quant worker only reads and
-- writes rows. The worker's SQL is tested against a database built from this
-- file, and PersonalQuant.IntegrationTests.ResearchBoundaryTests fails if the
-- columns, types, defaults or check constraints here drift from the migrated
-- table. Change the migration first, then this file to match.
CREATE SCHEMA IF NOT EXISTS research;

CREATE TABLE research.jobs (
    id uuid NOT NULL,
    kind character varying(64) NOT NULL,
    payload jsonb NOT NULL,
    status character varying(16) NOT NULL,
    claimed_by character varying(256),
    claimed_at_utc timestamp with time zone,
    lease_until_utc timestamp with time zone,
    attempts integer NOT NULL DEFAULT 0,
    created_at_utc timestamp with time zone NOT NULL,
    finished_at_utc timestamp with time zone,
    error text,
    result jsonb,
    CONSTRAINT pk_jobs PRIMARY KEY (id),
    CONSTRAINT ck_jobs_attempts CHECK (attempts >= 0),
    CONSTRAINT ck_jobs_outcome CHECK ((status = 'succeeded' AND result IS NOT NULL AND finished_at_utc IS NOT NULL) OR (status = 'failed' AND error IS NOT NULL AND finished_at_utc IS NOT NULL) OR (status IN ('queued', 'running') AND finished_at_utc IS NULL)),
    CONSTRAINT ck_jobs_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed'))
);

CREATE INDEX ix_jobs_status_created_at_utc ON research.jobs (status, created_at_utc);
