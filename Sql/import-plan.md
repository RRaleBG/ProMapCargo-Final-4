# OSM import plan

Do not import the whole planet directly into the application tables.

Recommended pipeline:

1. Download a regional `.osm.pbf` extract (for example Serbia/Balkans).
2. Extract relevant ways/tags with osmium/pyrosm/osmium-tool or a dedicated ETL.
3. Normalize OSM tags into `road_restrictions`.
4. Preserve `osm_way_id` for traceability.
5. Store road geometry as EPSG:4326.
6. Index geometry with GiST.
7. Keep `updated_at_utc` and source metadata.
8. Refresh incrementally or rebuild by region.

Important OSM tags to map:
- maxheight
- maxweight
- maxweight:hgv
- maxwidth
- maxlength
- maxaxleload
- hgv
- goods
- hazmat
- hazmat:*
- access
- vehicle
- motor_vehicle
- maxspeed
- conditional restrictions (`*:conditional`)

The importer must parse conditional syntax rather than treating it as a static value.

For a commercial-grade navigation product, keep an audit/source field for every imported restriction and do not assume OSM coverage is complete.
