# MapLibre Skill

Use MapLibre GL JS.

Never introduce Leaflet unless explicitly requested.

Architecture:

MapLibre map
    |
    +-- PMTiles source
    |
    +-- GeoJSON route sources
    |
    +-- navigation layers
    |
    +-- warning/restriction layers

Known route sources:

route-main
route-alternative
route-restrictions
route-warnings

Before changing map code:

Search:

promap-map-layers.js
navigation.js
promap-map-enhancements.js
promap-dark.json

Do not initialize multiple MapLibre maps for the same navigation page.

Do not create layers before their sources exist.

Do not use string values where MapLibre expects numeric values.

Validate style JSON after changes.
