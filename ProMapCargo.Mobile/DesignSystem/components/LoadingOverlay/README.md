# LoadingOverlay
Full-screen wait state over the map while navigation starts: a `SurfaceScrim` grid (ZIndex 100, not input-transparent) with a centred 68×68 PmMapGlassCard holding the ActivityIndicator (`Brand`), then the product name (PmSectionTitleLabel) and one muted status line ending in an ellipsis character (*Pokretanje navigacije…*). Stack spacing 14.

## Consumer provides
The status text and when to hide it. Use it for blocking starts only; refreshes use RefreshView with `RefreshColor=Brand`.
