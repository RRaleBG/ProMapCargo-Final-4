# MapControls
Buttons that float over the live map: PmMapFloatingButton (a 52px icon disc) and PmMapMiniButton (a 44-high text pill).

## When to use
- **PmMapFloatingButton**: ImageButton with `nav_recenter.svg` (recenter and reroute) or `nav_voice.svg` (repeat the voice prompt), stacked right-aligned, spacing 12. `MapGlass` fill, `StrokeStrong`, radius 26, padding 13. Also reused as the 52px back and settings disc in page headers, with `BackgroundColor=SurfaceOverlay`.
- **PmMapMiniButton**: short text actions on the map and in the route sheet (*Zatvori*, *Moja lokacija*, *⇅*). `MapGlass`, `TextPrimary` 13px semibold, radius 22.

## Consumer provides
`Command`, `Source` (ImageButton) or `Text`, and a Serbian `SemanticProperties.Description` on every icon button.

## Do / don't
- Keep controls lower right, in thumb reach. The overlay grid is `InputTransparent`; each control sets `InputTransparent="False"`.
- Glass (`MapGlass`) only over the map; on plain screens use SurfaceOverlay.
