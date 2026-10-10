# TabBar
The Shell TabBar with two destinations, Operacije (Dashboard) and Navigacija (map), using `ic_tab_operations.svg` and `ic_tab_navigation.svg`.

Set by the implicit `Shell` style: `TabBarBackgroundColor=SurfaceElevated`, selected icon and title `Brand`, unselected `TextTertiary`, `NavBarIsVisible=False` everywhere. Each page draws its own header: PmPageTitleLabel, plus a 52px back or settings disc.

## Do / don't
- Keep it to these two tabs; Login, Maps and Settings are routes outside the TabBar.
- Tab titles are single Serbian nouns.
