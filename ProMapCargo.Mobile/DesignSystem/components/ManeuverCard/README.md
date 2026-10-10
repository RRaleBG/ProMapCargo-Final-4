# ManeuverCard
The TomTom-style guidance banner at the top of navigation: a large maneuver icon on a Brand tile, the distance to it, the road it leads onto, and a small "zatim" icon for the maneuver after.

A PmMapGlassCard with `Padding="0"` holding `Grid ColumnDefinitions="Auto,*,Auto"`:
- **Icon tile**: Border, `Brand` fill, `StrokeShape="RoundRectangle 19,0,19,0"`, 100 wide; a `Path` (64×64, `Fill=BrandOn`, `Aspect=Uniform`) whose `Data` comes from `ManeuverIconConverter`.
- **Distance**: OpenSansSemibold 36px `TextPrimary`, `LineHeight 1` — "350 m", "1.2 km". No "Za" prefix here.
- **Road**: OpenSansSemibold 16px `TextSecondary`, one line, tail-truncated. Falls back to the instruction text when the road has no name.
- **Then**: PmCaptionLabel "zatim" over a 40×40 `SurfaceInset` tile with a 24px `TextPrimary` icon. Hidden when there is no following maneuver.

## Icons
`Converters/ManeuverIconConverter.cs` maps the API maneuver type to a 24×24 vector arrow: `continue`, `left`, `right`, `slight-left`, `slight-right`, `sharp-left`, `sharp-right`, `u-turn` (to the left, right-hand traffic), `roundabout`, `arrive`. Aliases: `turn-left`, `turn-right`, `uturn`, `destination`, `depart`, `straight`. Unknown types draw `continue`. It returns a new Geometry per element, so two icons never share one instance.

## Consumer provides
`NextManeuverKind`, `NextManeuverDistanceShort`, `NextManeuverRoad`, `ThenManeuverKind`, `HasThenManeuver`, and `NextManeuverType` for the screen-reader description.

## Do / don't
- The distance is the figure a driver reads at a glance: never below 36px.
- No GPS coordinates, engine names or other diagnostics in the banner.
- Instructions stay in Serbian; the icon carries the direction, the text only names the road.
