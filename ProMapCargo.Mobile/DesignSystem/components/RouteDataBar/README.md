# RouteDataBar
The bottom glass strip in navigation with three readouts: arrival time, route summary, current speed.

PmMapGlassCard with `StrokeShape="RoundRectangle 18"` and `Grid ColumnDefinitions="*,*,*" ColumnSpacing="8"`; each cell is a PmRouteDataCaption (uppercase *DOLAZAK*, *RUTA*, *KM/H*) over a PmRouteDataLabel (14px, tail-truncated). The last cell is right-aligned.

## Consumer provides
`RouteEstimatedArrival`, `RouteSummary`, `CurrentSpeedText`; shown only while `HasRoute`.
