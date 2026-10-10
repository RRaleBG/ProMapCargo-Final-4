# Card
Three bordered surfaces: PmHeroBorder for the one featured block, PmCardBorder for every grouped section, PmCardInnerBorder for a nested panel.

## When to use
- **PmHeroBorder**: only the active-route block at the top of Dashboard. Gradient `HeroStart` → `SurfaceElevated` (top-left to bottom-right), `StrokeStrong` 1px, radius 24, padding 20.
- **PmCardBorder**: lists, settings groups, KPI tiles, the login form. `SurfaceOverlay` fill, `StrokeSubtle` 1px, radius 20, padding 16. List cards override padding to `16,4` and let rows pad themselves 12 vertically.
- **PmCardInnerBorder**: a panel inside a card. `SurfaceInset`, radius 14, padding 12.

## Consumer provides
One child layout, usually a `VerticalStackLayout` with Spacing 12–16. A section title (PmSectionTitleLabel or PmCaptionLabel) sits *outside* the card, 10 above it.

## Do / don't
- No shadows: depth comes from fill steps (Base → Overlay → Inset) and 1px strokes.
- Inside the hero, use PmHeroCaptionLabel (TextSecondary) for captions; plain PmCaptionLabel is too faint on the gradient.

```xml
<Border Style="{StaticResource PmCardBorder}">
    <VerticalStackLayout Spacing="12"> … </VerticalStackLayout>
</Border>
```
