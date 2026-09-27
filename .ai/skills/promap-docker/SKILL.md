# ProMap Cargo — Docker

## Services
- `promap-api`
- `promap-postgres`
- `promap-osrm`

## Workflow
Use Docker Compose for integration checks:
- inspect service status
- inspect logs
- verify health endpoints
- verify API → Postgres connectivity
- verify API → OSRM connectivity
- rebuild only the affected service when possible

## Data
Do not bake large OSM/PMTiles datasets into Git.
The project intentionally ignores `/maps`, `/data`, OSM PBF files and generated OSRM data.
Prefer mounted datasets and reproducible import/build scripts.
