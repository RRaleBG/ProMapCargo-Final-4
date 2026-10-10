# Chip
Small status pills: PmChip (success, the default), PmChipInfo, PmChipWarning, the square-cornered PmMapBadgeLabel over the map, and PmLaneChip for lane guidance.

## When to use
- **PmChip** + **PmChipLabel**: record status in list rows and the hero (*Aktivno*, route safety). `StatusSuccessBg` with `StatusSuccess` 11px semibold text, padding 10,5, radius 12.
- **PmChipInfo**: neutral facts such as the routing engine. **PmChipWarning**: states that need attention; the login error box is a PmChipWarning holding a PmErrorLabel.
- **PmMapBadgeLabel**: a Label (no Border) on the maneuver card for the route engine.
- **PmLaneChip**: one per lane. `SurfaceInset`, `StrokeStrong`, radius 10, padding 6,4. Highlight the lanes to take.

## Consumer provides
A Border with the chip style and a Label with the matching `…Label` style. Text: one or two words, tail-truncated.

## Do / don't
- Colour never carries the state alone: the word says it.
- Wrap chips in a `FlexLayout Wrap="Wrap"` with `Margin="0,0,8,8"` on each.
