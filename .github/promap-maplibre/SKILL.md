# ProMap Cargo — MapLibre / PMTiles

## Invariants
- One MapLibre map instance.
- One PMTiles base-map source.
- GeoJSON route sources for main/alternative/restrictions/warnings.
- Layer creation must occur only after the relevant source exists.
- Destruction must remove listeners, layers and sources without recursion.

## Files
- `promap-map-layers.js`
- `promap-map-enhancements.js`
- `navigation.js`
- `promap-dark.json`

## Debug checklist
When a source/layer error appears:
1. Search for every `getSource`, `addSource`, `removeSource`, `getLayer`, `addLayer`, `removeLayer`.
2. Confirm source registration precedes layer creation.
3. Confirm no duplicate map initialization.
4. Check style JSON for numeric `line-width`/interpolate stops and valid color strings.
5. Check CSP/worker configuration only after JavaScript lifecycle issues are ruled out.

Do not add a theme/layer switcher unless the feature is explicitly requested.
