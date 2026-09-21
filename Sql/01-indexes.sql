CREATE EXTENSION IF NOT EXISTS postgis;

CREATE INDEX IF NOT EXISTS ix_road_restrictions_geometry
ON road_restrictions USING GIST (geometry);

CREATE INDEX IF NOT EXISTS ix_road_restrictions_osm_way_id
ON road_restrictions (osm_way_id);

CREATE INDEX IF NOT EXISTS ix_road_restrictions_type
ON road_restrictions (restriction_type);
