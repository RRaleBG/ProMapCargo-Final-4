# ListRow
The repeated row inside a list card: semibold title over a muted subtitle on the left, one trailing element on the right, a PmDivider below.

## Anatomy
`Grid Padding="0,12" ColumnDefinitions="*,Auto" ColumnSpacing="12"`. Left: `VerticalStackLayout Spacing="2"` with PmBodyLabel (+ `FontFamily="OpenSansSemibold"`) and PmMutedLabel. Right: one of Switch, PmChip, PmAccentLabel (a percentage), PmTonalButton. Row 2 holds the `PmDivider` (`Margin="0,12,0,0"`).

## Consumer provides
`BindableLayout.ItemsSource`, an `ItemTemplate` as above, and a `BindableLayout.EmptyView`: a PmMutedLabel with `Padding="0,14"` saying what is missing (*Nema aktivnih naloga.*).

## Do / don't
- The parent PmCardBorder uses `Padding="16,4"` (Settings: `16,6`).
- One trailing element per row. Titles are data (registration, order number), not labels.
- Switches need `SemanticProperties.Description` equal to the row title.
