-- ============================================================
-- ProMapCargo routing graph maintenance
--
-- Run manually with psql, for example:
--   docker exec -i promap-postgres psql -U promap -d promapcargo < Sql/06-graph-maintenance.sql
--
-- Sections 1 and 4 are safe. Sections 2 and 3 change data / lock the
-- table and must be run deliberately (see the comments).
-- ============================================================

-- ------------------------------------------------------------
-- 1. Which graph versions exist and how big are they? (read only)
--    The importer only cleans the version it is importing, so older
--    versions accumulate. road_edges scans get slower with every one.
-- ------------------------------------------------------------
SELECT
    v.graph_version,
    v.status,
    v.created_at,
    v.activated_at,
    (SELECT count(*) FROM road_edges e WHERE e.graph_version = v.graph_version) AS edges
FROM routing_graph_versions v
ORDER BY v.graph_version;

-- ------------------------------------------------------------
-- 2. Remove an OLD graph version (DESTRUCTIVE, run per version).
--
--    Replace :old_version with a graph_version that is NOT the active
--    one (the active one is the newest row with status='ready' and
--    activated_at IS NOT NULL). Take a backup first (pg_dump).
--
--    Example in psql:
--      \set old_version 1790000000
--      (then run the block below)
-- ------------------------------------------------------------
-- BEGIN;
-- DELETE FROM compiled_turn_restrictions WHERE graph_version = :old_version;
-- DELETE FROM turn_restrictions          WHERE graph_version = :old_version;
-- DELETE FROM road_edges                 WHERE graph_version = :old_version;
-- DELETE FROM osm_way_nodes              WHERE graph_version = :old_version;
-- DELETE FROM osm_ways                   WHERE graph_version = :old_version;
-- DELETE FROM osm_nodes                  WHERE graph_version = :old_version;
-- DELETE FROM routing_graph_versions     WHERE graph_version = :old_version;
-- COMMIT;

-- ------------------------------------------------------------
-- 3. Physically order road_edges by space (LOCKS THE TABLE, can take
--    minutes; run while nobody is routing). Makes bounding-box reads
--    touch contiguous pages instead of random ones.
--    Run again after a new import or after deleting many rows.
-- ------------------------------------------------------------
-- CLUSTER road_edges USING ix_road_edges_geom;

-- ------------------------------------------------------------
-- 4. Refresh planner statistics (safe).
-- ------------------------------------------------------------
ANALYZE road_edges;
ANALYZE turn_restrictions;

-- ------------------------------------------------------------
-- 5. Check that bounding-box reads use the spatial index (read only).
--    Expect "Bitmap Heap Scan" / "Index Scan" on ix_road_edges_geom,
--    NOT "Seq Scan". Replace the version with the active one.
-- ------------------------------------------------------------
-- EXPLAIN (ANALYZE, BUFFERS)
-- SELECT id, ST_X(ST_StartPoint(geom))
-- FROM road_edges
-- WHERE graph_version = 1791617199
--   AND routable
--   AND geom && ST_MakeEnvelope(20.0, 44.7, 20.1, 44.8, 4326);
