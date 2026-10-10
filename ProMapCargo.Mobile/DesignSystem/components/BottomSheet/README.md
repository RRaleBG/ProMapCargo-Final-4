# BottomSheet
The route-planning sheet that slides up over the map: PmBottomPanel holding a title row, two address fields, suggestions, an option switch and the primary action.

PmBottomPanel: `MapSheet` fill, `StrokeStrong`, padding 16, radius 22 (overrides the style's 24), stack spacing 10. It enters with `TranslationY` 28 → 0 and `Opacity` 0 → 1, and is `InputTransparent` while hidden.

## Order
1. Title (PmSectionTitleLabel) and PmMapMiniButton *Zatvori*
2. Start field with *Moja lokacija*; destination field with *⇅* (see TextField)
3. Suggestions: `ScrollView MaximumHeightRequest="170"`, each item padding 6,8, a PmRouteDataLabel title over a one-line PmRouteDataCaption
4. Switch and label for routing options
5. Inline error: 12px `StatusDanger`
6. PmPrimaryButton *Izračunaj rutu*

## Consumer provides
`StartQuery`, `DestinationQuery`, `Suggestions` and `SelectSuggestionCommand`, option bindings, `ErrorMessage`, `CalculateRouteCommand`.
