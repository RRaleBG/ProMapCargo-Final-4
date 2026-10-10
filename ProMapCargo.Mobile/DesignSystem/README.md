ProMap Cargo Mobile is the .NET MAUI driver app for truck navigation and dispatch: a dark, map-first UI tuned for reading at a glance in a moving cab. Every value here is a resource key in `Resources/Styles/Colors.xaml` or a `Pm*` style in `Resources/Styles/Styles.xaml`; build new screens from those keys, never from literal hex or sizes.

## Principles

- **The map is the product.** Navigation runs full-bleed; everything over it is translucent glass (`MapGlass`, `MapSheet`) with a 1px `StrokeStrong` edge, grouped at the top (what's next) and bottom (status, controls).
- **One accent, one action.** `Brand` green marks the single committing action, the selected tab, toggles and the distance to the next maneuver. Nothing else is filled green.
- **Dark only.** The app ships one theme ("Tema: tamna, prilagođena vožnji"). `App.xaml.cs` forces `UserAppTheme = AppTheme.Dark`, so every style uses `StaticResource`; don't add `AppThemeBinding`.
- **Big, calm targets.** 44 is the minimum for anything tappable (`size-touch-min`); primary actions and map discs are 52.

## Content

- **Language: Serbian (Latin script)** with full diacritics: *Izračunaj rutu*, *Podešavanja*, *Sledeći manevri*. Formal *Vi* for instructions (*Prijavite se*, *Izračunajte rutu da biste videli manevre.*), imperative verbs on buttons (*Otvori*, *Zatvori*, *Prijavi se*).
- **Sentence case** for titles, buttons and body. Captions (`PmCaptionLabel`) are written in sentence case and set uppercase by the style; data captions in navigation are typed uppercase (*DOLAZAK*, *RUTA*, *KM/H*).
- **Empty states** say what is missing, in one sentence ending with a period: *Nema vozila.*, *Nema aktivnih tura.* Status and progress lines end with the ellipsis character: *Pokretanje navigacije…*
- **Numbers**: dates `dd.MM. HH:mm` (*GPS 10.10. 14:32*), distances *Za 350 m*, percentages with no space (*62%*).
- No emoji. The only glyph used as a label is `⇅` (swap start and destination).

## Colour

Ground and surfaces step up in lightness, never by shadow:

| Layer | Token | Used for |
| --- | --- | --- |
| Ground | `SurfaceBase` | Page, Shell, WebView backdrop |
| Bar | `SurfaceElevated` | TabBar; hero gradient end |
| Card | `SurfaceOverlay` | `PmCardBorder`, header discs |
| Inset | `SurfaceInset` | Fields, inner panels, lane chips |
| Glass | `MapGlass` (90%) / `MapSheet` (95%) | Anything over the map |
| Scrim | `SurfaceScrim` (85%) | Blocking loading overlay |

- Text: `TextPrimary` for titles, body and values; `TextSecondary` for supporting lines (`PmMutedLabel`); `TextTertiary` for captions and placeholders only; `TextAccent` for road names, progress and ghost buttons.
- Strokes: `StrokeSubtle` on cards and dividers, `StrokeStrong` on anything interactive or floating (fields, glass, hero).
- Status always pairs a 12% tint with its full colour: `StatusSuccess` on `StatusSuccessBg`, `StatusInfo` on `StatusInfoBg`, `StatusWarning` on `StatusWarningBg`. `StatusDanger` is used as text (`PmErrorLabel`).
- **MAUI colours are `#AARRGGBB`.** `StatusSuccessBg` is `#1F10B981` in XAML and `#10b9811f` here; convert the alpha byte when moving between XAML and CSS.
- Keys under *Legacy* in the token notes (`Primary`, `Gray*`, `Magenta`, `MidnightBlue`…) come from the MAUI template and only feed the implicit control styles. Use the v2 keys in new XAML.
- Android splash and status bar read `Platforms/Android/Resources/values/colors.xml`, which duplicates these values (`colorWarning` = `StatusWarning`, `colorError` = `StatusDanger`). Keep it in step when `Colors.xaml` changes.

## Type

One family, **Open Sans**, registered in `MauiProgram.cs` as `OpenSansRegular` (400) and `OpenSansSemibold` (600). No other weights or faces.

- `PmPageTitleLabel` 26/1.15 semibold, one per screen, `HeadingLevel Level1`.
- `PmSectionTitleLabel` 17/1.2 semibold above each card and on map cards.
- `PmValueLabel` 22 semibold for KPI counts; the next-maneuver distance is 20 semibold in `Brand`.
- `PmBodyLabel` 14/1.3; list-row titles add `FontFamily="OpenSansSemibold"`.
- `PmMutedLabel` 13/1.35 in `TextSecondary`; `PmAccentLabel` and `PmErrorLabel` share 13px.
- `PmCaptionLabel` 11 semibold, uppercase, `CharacterSpacing 1`, `TextTertiary`.
- Labels that can overflow use `LineBreakMode="TailTruncation"`; instructions and errors use `WordWrap`.

## Layout and spacing

- Scrolling pages: `Padding="20,24,20,32"`, `MaximumWidthRequest="720"` (Login: `24,56,24,32`, max 480), sections `Spacing="22"` (Settings 20).
- Section = title (PmSectionTitleLabel or PmCaptionLabel) + card, `Spacing="10"`.
- Cards pad 16; list cards pad `16,4` and each row pads `0,12` with a `PmDivider` between.
- Map overlays inset `16,14,16,20`; controls stack right-aligned with spacing 12.
- Spacing tokens are the literal values the XAML uses: 2, 4, 6, 8, 10, 12, 14, 16, 20, 22, 24, 32. Pick from them; don't invent 18 or 28.

## Shape and depth

- Radii grow with the container: chips 12, controls and fields 14, cards and glass 20, hero and sheets 24 (route sheet 22). Round controls take half their height: 22 for 44, 26 for 52.
- Every bordered surface has a 1px stroke (`size-stroke`). There are no drop shadows in the v2 styles; the template `Shadow` style is unused.
- The only gradient is the hero: `HeroStart` to `SurfaceElevated`, top-left to bottom-right. The recenter and voice icons carry their own `#34D399 → #2DD4BF` gradient.

## States and motion

- Disabled: `ImageButton` drops to `opacity-disabled` (0.5); template controls switch to `Gray600` fills.
- Switch: `OnColor` `Brand`, thumb `White`; off thumb `Gray500`.
- Busy: `ActivityIndicator` in `Brand`; pull-to-refresh with `RefreshColor=Brand`; blocking starts use the LoadingOverlay.
- The route sheet slides in from `TranslationY 28` with fade; nothing else animates.

## Accessibility

- Every `ImageButton` and `Switch` has a Serbian `SemanticProperties.Description` (*Podešavanja*, *Nazad*, *Glasovno uputstvo*).
- Text pairs meet 4.5:1 on their surfaces, with three exceptions kept from the source and flagged in the token notes: `TextTertiary` captions and `StatusInfo` chips inside the hero gradient (3.33:1 and 3.84:1), and `StrokeStrong` field borders (1.70:1 on SurfaceOverlay, under the 3:1 control-border floor).
- Status chips always carry a word; colour is never the only signal.

## Iconography

- App glyphs in `assets/Icons` are 24×24 single-path SVGs filled `#FFFFFF` (Material-style back, map, settings, tab icons), tinted by Shell for tabs. Use them through `ImageButton`/`Image` `Source="ic_*.svg"`.
- The two map discs use 64×64 duotone illustrations, `nav_recenter.svg` and `nav_voice.svg`, with the brand gradient baked in. Don't recolour them.
- The app icon and splash in `assets/Logos` are the only brand marks; the login screen shows `splash.svg` at 88×88. The splash colour is `SurfaceBase`.
