# ManeuverCard
The top-of-screen card in navigation: next instruction, distance to it, the routing engine and the current place.

A PmMapGlassCard (`MapGlass`, `StrokeStrong`, radius 20, padding 14) holding a 2×2 grid (`ColumnDefinitions="*,Auto"`, spacing 10 and 6):
- instruction: PmSectionTitleLabel, word-wrapping
- engine: PmMapBadgeLabel, top right
- distance: OpenSansSemibold 20px in `Brand`
- current location: PmMutedLabel, right, max width 160, tail-truncated.

## Consumer provides
`NextManeuverInstruction`, `NextManeuverDistanceText`, `RouteEngine`, `CurrentLocationText`.

## Do / don't
The distance is the figure a driver reads at a glance: never below 20px, never any colour but Brand.
