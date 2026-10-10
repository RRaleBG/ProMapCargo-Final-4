# TextField
A PmEntry inside a PmFieldBorder, with an uppercase PmCaptionLabel above it: the only text-input pattern in the app.

## Anatomy
`VerticalStackLayout Spacing="6"` → PmCaptionLabel (the field name) → `Border Style=PmFieldBorder` (`SurfaceInset`, `StrokeStrong` 1px, radius 14, padding 14,0) → `Entry Style=PmEntry` (48 high, 15px, `TextPrimary`, placeholder `TextTertiary`, transparent).

In the route sheet the caption is dropped and the placeholder names the field; a PmMapMiniButton can sit beside it in a `ColumnDefinitions="*,Auto"` grid with spacing 8.

## Consumer provides
`Text` binding, `Placeholder` (an example value, *ime@firma.rs*), `Keyboard`, `ReturnType` and `ReturnCommand` on the last field, and `SemanticProperties.Description`.

## Errors
One error for the whole form, under the fields: a PmChipWarning box (`HorizontalOptions="Fill"`, padding 12,10) holding a PmErrorLabel, shown via `StringNotEmptyConverter`.

## Note
`StrokeStrong` is 1.70:1 on SurfaceOverlay, so the border alone does not meet 3:1; the inset fill and the caption carry the affordance.
