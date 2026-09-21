-- Count by restriction type
SELECT restriction_type, count(*)
FROM road_restrictions
GROUP BY restriction_type
ORDER BY count(*) DESC;

-- Inspect imported OSM records
SELECT id, osm_way_id, name, restriction_type,
       maxheight_meters, maxweight_tons, maxwidth_meters,
       maxlength_meters, maxaxleload_tons, hgv_ban, hazmat_ban
FROM road_restrictions
ORDER BY id
LIMIT 100;

-- Spatial query example: restrictions near a route geometry
-- Replace the WKT with the actual route LineString.
SELECT id, osm_way_id, name, restriction_type
FROM road_restrictions
WHERE ST_DWithin(
  geometry::geography,
  ST_GeomFromText('LINESTRING(20.45 44.81,20.46 44.82)',4326)::geography,
  100
);
