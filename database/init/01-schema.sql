-- Runs once, on first start of an empty Postgres data volume (see docker-compose.yml).
--
-- Everything here is infrastructure that must exist *before* EF Core migrations run.
-- Tables are NOT created here — EF Core migrations own the schema. Keep this file limited
-- to extensions, schemas and roles.

-- gen_random_uuid(), used as the database-side default for primary keys.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Accent- and case-insensitive search on product names, customer names and serial numbers.
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Case-insensitive text, used for email addresses. "Ali@Shop.ae" and "ali@shop.ae" are the same
-- person, and a case-sensitive unique index would cheerfully let both accounts exist.
CREATE EXTENSION IF NOT EXISTS citext;

-- All application tables live in "techstorepro"; the EF migrations history table lives here too.
-- "public" is left empty so extensions and application data never collide.
CREATE SCHEMA IF NOT EXISTS techstorepro AUTHORIZATION techstorepro;

ALTER DATABASE techstorepro SET search_path TO techstorepro, public;
