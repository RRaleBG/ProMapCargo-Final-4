# Button
Five button styles from Styles.xaml: PmPrimaryButton, PmSecondaryButton, PmGhostButton, PmTonalButton and PmIconButton, all OpenSansSemibold.

## When to use
- **PmPrimaryButton**: the one committing action on a screen or sheet (*Izračunaj rutu*, *Prijavi se*, *Otvori navigaciju*). `Brand` fill, `BrandOn` text, 52 high, radius 14, 15px. Full width inside cards and sheets.
- **PmSecondaryButton**: a second, navigating action inside a card (*Upravljanje mapama*). `BrandSoft` fill, `TextPrimary` text, 48 high.
- **PmGhostButton**: low-emphasis toggles and links (*Prikaži dijagnostiku rute*). Transparent, `TextAccent` text, 44 high, `HorizontalOptions="Start"`.
- **PmTonalButton**: a compact action at the end of a list row (*Otvori*). `StatusSuccessBg` fill, `StatusSuccess` 13px text, 40 high; the row supplies the extra touch height.
- **PmIconButton**: a 44×44 disc holding one glyph (`FontSize 18`), on `SurfaceOverlay`.

## Consumer provides
`Text` (Serbian, sentence case, verb first) and `Command` or `Clicked`. Icon buttons need `SemanticProperties.Description`.

## Do / don't
- One PmPrimaryButton per view. Never two Brand fills side by side.
- Don't set `FontSize` or `CornerRadius` inline; pick the style.
- Over the map use PmMapMiniButton / PmMapFloatingButton (see MapControls), not these.

```xml
<Button Style="{StaticResource PmPrimaryButton}" Text="Izračunaj rutu" Command="{Binding CalculateRouteCommand}" />
```
