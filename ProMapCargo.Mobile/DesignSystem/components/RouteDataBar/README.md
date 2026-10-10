# RouteDataBar
The bottom trip bar in navigation: arrival time, time left and distance left, with the current speed in a round bubble to its left.

**Trip bar**: PmMapGlassCard with `StrokeShape="RoundRectangle 18"` and `Grid ColumnDefinitions="*,*,*" ColumnSpacing="8"`. Each cell is a PmRouteDataCaption (*DOLAZAK*, *PREOSTALO*, *DO CILJA*) over a value in OpenSansSemibold 20px `TextPrimary`; the middle cell is centred, the last right-aligned.

**Speed bubble**: a 68×68 `Ellipse` Border, `MapGlass` fill, 2px `StrokeStrong`, bottom-left above the bar: the speed in OpenSansSemibold 24px over "km/h" at 10px `TextSecondary`.

## Consumer provides
`ArrivalTimeText` (HH:mm), `RemainingTimeText` ("1 h 12 min", "8 min"), `RemainingDistanceText` ("94 km", "850 m"), `CurrentSpeedText` (a bare number). Shown only while `HasRoute`. Remaining values are recalculated on every GPS update from the next maneuver's position along the route.
