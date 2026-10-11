# Syncfusion .NET MAUI Theming Resources

**⚠️ MANDATORY:** After making your theming choice, you MUST consult **Skill: syncfusion-maui-theming** for detailed implementation guidance before proceeding to Stage 5:

## Quick Reference: Syncfusion .NET MAUI Themes

| Platform Focus | Syncfusion MAUI Theme | Setup Location | VisualTheme Property |
|---|---|---|---|
| **Cross-Platform (Recommended)** | Material Design | `App.xaml` | `MaterialLight` or `MaterialDark` |
| **iOS/macOS Focused** | Cupertino Design | `App.xaml` | `CupertinoLight` or `CupertinoDark` |
| **Platform-Specific** | Mixed | `App.xaml` with `OnPlatform` | Platform-dependent |
| **Custom Branding** | Material/Cupertino Base + Overrides | `App.xaml` + ResourceDictionary | Base theme + custom keys |

**Setup Requirements:**
- Namespace: `xmlns:syncTheme="clr-namespace:Syncfusion.Maui.Themes;assembly=Syncfusion.Maui.Core"`
- Location: `Application.Resources` → `ResourceDictionary.MergedDictionaries`
- Install: `Syncfusion.Maui.Core` NuGet package (included with any Syncfusion MAUI control)
- Apply at App.xaml level (before any pages render) for consistent styling across all Syncfusion MAUI controls

## By Use Case:

### Basic Theme Setup & Application
📄 **Refer to:** **Skill: syncfusion-maui-theming** → **Applying Themes**
- Installing and configuring `SyncfusionThemeResourceDictionary` in App.xaml
- Available built-in themes (MaterialLight, MaterialDark, CupertinoLight, CupertinoDark)
- Platform-specific theme configuration with `OnPlatform`
- Automatic merging of all Syncfusion MAUI control styles
- VisualTheme property usage and valid values
- Common setup mistakes and troubleshooting

### Implementing Light/Dark Mode
📄 **Refer to:** **Skill: syncfusion-maui-theming** → **Theme Switching**
- Dynamically changing themes at runtime (no app restart)
- ViewModel pattern for theme management (ThemeService)
- Dark mode toggle implementation
- Syncing with system/OS theme preferences
- User preference persistence (preferences storage)
- Edge cases and troubleshooting for theme transitions

### Customizing Theme Colors for Branding
📄 **Refer to:** **Skill: syncfusion-maui-theming** → **Overriding Themes**
- Overriding specific theme color keys
- Theme key naming conventions (Sf{ControlName}{Element}{Property})
- Control-specific vs global color keys
- Merged dictionary layer order and hierarchy
- Custom primary colors, accent colors, semantic colors
- Maintaining custom theme overrides across updates

### Creating Custom Themes from Scratch
📄 **Refer to:** **Skill: syncfusion-maui-theming** → **Creating Custom Themes**
- Building themes from scratch (not recommended unless required)
- Theme key registration patterns
- Custom color scheme definition
- Complete theme implementation reference
- Corporate branding theme examples
- High contrast accessibility themes
- Theme documentation and maintenance

### Theme Key Reference & Discovery
📄 **Refer to:** **Skill: syncfusion-maui-theming** → **Theme Keys Reference**
- Complete reference of all theme key names
- Key naming convention: Sf{ControlName}{Element}{Property}
- Finding keys for any specific control
- Common control keys by category (buttons, inputs, charts, grids, etc.)
- Official Syncfusion documentation references
- Debugging theme application issues

## MAUI Theming Best Practices

**1. Always Use SyncfusionThemeResourceDictionary**
- Single point of truth for all Syncfusion MAUI control styling
- Automatic merging saves maintenance burden
- No need for per-control theme imports

**2. Choose Correct Theme for Your Platforms**
- **MaterialLight/MaterialDark:** Cross-platform apps, Android/Windows primary
- **CupertinoLight/CupertinoDark:** iOS/macOS focused apps
- **OnPlatform Mixed:** Premium apps with platform-specific design

**3. Override Via ResourceDictionary, Not Direct XAML**
- ❌ Don't: Manually set colors on individual controls
- ✅ Do: Override theme keys in App.xaml ResourceDictionary
- Reason: Centralized, maintainable, respects theming hierarchy

**4. Test on All Target Platforms**
- Test MaterialLight/MaterialDark on iOS, Android, Windows, macOS
- Verify safe areas (iOS notches, Android system UI)
- Check contrast ratios (WCAG AA minimum 4.5:1)
- Test theme switching doesn't break layout

**5. Document Custom Theme Decisions**
- Document why custom theme was chosen
- List all custom theme key overrides
- Maintain version compatibility information
- Keep branding guidelines aligned with theme

## Quick Implementation Template

**App.xaml - Material Design (Cross-Platform):**
```xml
<Application xmlns:syncTheme="clr-namespace:Syncfusion.Maui.Themes;assembly=Syncfusion.Maui.Core"
             x:Class="YourApp.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**App.xaml - Platform-Specific (iOS: Cupertino, Android: Material):**
```xml
<Application xmlns:syncTheme="clr-namespace:Syncfusion.Maui.Themes;assembly=Syncfusion.Maui.Core"
             x:Class="YourApp.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <OnPlatform x:TypeArguments="ResourceDictionary">
                    <On Platform="iOS, MacCatalyst">
                        <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="CupertinoLight"/>
                    </On>
                    <On Platform="Android, WinUI">
                        <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
                    </On>
                </OnPlatform>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**App.xaml - Custom Color Overrides:**
```xml
<Application xmlns:syncTheme="clr-namespace:Syncfusion.Maui.Themes;assembly=Syncfusion.Maui.Core"
             x:Class="YourApp.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
            </ResourceDictionary.MergedDictionaries>
            
            <!-- Custom color overrides for branding -->
            <Color x:Key="SfPrimaryBrush">#FF0066FF</Color>
            <Color x:Key="SfAccentBrush">#FFFF6600</Color>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```
