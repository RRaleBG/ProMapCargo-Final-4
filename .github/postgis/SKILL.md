# PostGIS Routing Skill

ProMap Cargo uses PostgreSQL/PostGIS.

Routing graph concepts:

osm_nodes
road_edges

Important:

Do not assume the graph is empty.

Before changing routing SQL:

- inspect schema
- inspect indexes
- inspect geometry SRID
- inspect node/edge relationships
- inspect routing directionality
- inspect restrictions

Prefer bounded diagnostic queries.

Avoid unbounded COUNT(*) on very large routing tables.

Routing must support:

truck dimensions
weight
axle load
hazmat
road restrictions
speed
direction
snap radius

OSRM is fallback, not the primary truck-aware graph.
