-- The research schema as the backend's migrations create it
-- (ResearchBoundary, ExperimentStore).
--
-- The backend owns these tables (ADR-016); the quant worker only reads and
-- writes rows. The worker's SQL is tested against a database built from this
-- file, and PersonalQuant.IntegrationTests.ResearchBoundaryTests fails if any
-- table's columns, types, defaults, constraints or indexes here drift from the
-- migrated schema. Change the migration first, then this file to match
-- (`dotnet ef migrations script` prints the DDL).
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

CREATE TABLE research.experiments (
    id uuid NOT NULL,
    name character varying(128) NOT NULL,
    hypothesis character varying(4000),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_experiments PRIMARY KEY (id)
);

CREATE TABLE research.runs (
    id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    job_id uuid,
    status character varying(16) NOT NULL,
    strategy character varying(64) NOT NULL,
    universe_code character varying(64) NOT NULL,
    universe_as_of date,
    membership character varying(16) NOT NULL,
    dataset_id character varying(64) NOT NULL,
    dataset_version integer NOT NULL,
    schema_version integer NOT NULL,
    content_hash character varying(64) NOT NULL,
    feature_version character varying(128),
    factor_version character varying(128) NOT NULL,
    model_version character varying(128),
    params jsonb NOT NULL,
    params_hash character varying(64) NOT NULL,
    code_commit character varying(64),
    random_seed bigint,
    research_engine character varying(64) NOT NULL,
    research_engine_version character varying(64) NOT NULL,
    trial_index integer,
    trial_count integer,
    reproduces_run_id uuid,
    started_at_utc timestamp with time zone NOT NULL,
    finished_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_runs PRIMARY KEY (id),
    CONSTRAINT ck_runs_status CHECK (status IN ('succeeded', 'failed')),
    CONSTRAINT ck_runs_trial CHECK ((reproduces_run_id IS NULL AND trial_index IS NOT NULL AND trial_count IS NOT NULL AND trial_index >= 1 AND trial_count >= trial_index) OR (reproduces_run_id IS NOT NULL AND trial_index IS NULL AND trial_count IS NULL)),
    CONSTRAINT ck_runs_window CHECK (finished_at_utc >= started_at_utc),
    CONSTRAINT fk_runs_experiments FOREIGN KEY (experiment_id) REFERENCES research.experiments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_runs_jobs FOREIGN KEY (job_id) REFERENCES research.jobs (id) ON DELETE SET NULL,
    CONSTRAINT fk_runs_reproduces FOREIGN KEY (reproduces_run_id) REFERENCES research.runs (id) ON DELETE RESTRICT
);

CREATE TABLE research.run_artifacts (
    run_id uuid NOT NULL,
    kind character varying(32) NOT NULL,
    path character varying(512) NOT NULL,
    sha256 character varying(64) NOT NULL,
    CONSTRAINT pk_run_artifacts PRIMARY KEY (run_id, kind, path),
    CONSTRAINT fk_run_artifacts_runs FOREIGN KEY (run_id) REFERENCES research.runs (id) ON DELETE CASCADE
);

CREATE TABLE research.run_metrics (
    run_id uuid NOT NULL,
    name character varying(64) NOT NULL,
    split character varying(16) NOT NULL,
    value double precision,
    CONSTRAINT pk_run_metrics PRIMARY KEY (run_id, name, split),
    CONSTRAINT fk_run_metrics_runs FOREIGN KEY (run_id) REFERENCES research.runs (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX ux_experiments_name ON research.experiments (name);
CREATE INDEX ix_runs_dataset ON research.runs (dataset_id, dataset_version);
CREATE INDEX ix_runs_reproduces_run_id ON research.runs (reproduces_run_id);
CREATE UNIQUE INDEX ux_runs_experiment_trial ON research.runs (experiment_id, trial_index);
CREATE UNIQUE INDEX ux_runs_job_id ON research.runs (job_id);
