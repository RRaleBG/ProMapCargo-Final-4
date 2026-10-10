# KpiTile
A count with an uppercase caption, three to a row on Dashboard.

`Grid ColumnDefinitions="*,*,*" ColumnSpacing="12"`; each tile is a PmCardBorder with `Padding="14"` holding PmValueLabel (22px semibold) over PmCaptionLabel, spacing 2.

## Consumer provides
A number binding (`Vehicles.Count`) and a one-word plural caption. No units, no trend arrows.
