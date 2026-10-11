# Stage 4: Theming & Design System Selection

**Purpose:** Understand design system trade-offs and lock theming decisions before code generation.

## Overview

This stage is about **decision-making clarity**, not code generation. You'll:

- **Confirm your Material Design 3 theme strategy** and understand MAUI theming fundamentals
- **Select Syncfusion MAUI theme colors** that align with Material 3 and your brand, not against it
- **Define color architecture** based on principles (perceptual uniformity, contrast, brand cohesion)
- **Establish spacing and typography scales** that respect readability and hierarchy
- **Plan responsive strategy** that matches your framework's conventions
- **Document token structure** so Stage 5 can generate consistent code

**Key Insight:** MAUI uses ResourceDictionary and Implicit Styles for theming. Stage 4 ensures your Syncfusion MAUI theme and design system align with cross-platform standards (iOS, Android, Windows, macOS), Material 3 design principles, and MAUI best practices.

**Output:** Design system decisions documented and ready for implementation in Stage 5.

---

## Table of Contents

1. [MAUI Theming Philosophy](#1-maui-theming-philosophy)
2. [Syncfusion MAUI Theme Alignment](#2-syncfusion-maui-theme-alignment)
3. [Color System Architecture](#3-color-system-architecture)
4. [Spacing & Typography Systems](#4-spacing--typography-systems)
5. [Responsive Strategy](#5-responsive-strategy)
6. [Motion & Accessibility Standards](#6-motion--accessibility-standards)
7. [Design System Token Architecture](#7-design-system-token-architecture)
8. [Syncfusion Component Integration](#8-syncfusion-component-integration)
9. [Load Your Framework Reference (MANDATORY)](#9-load-your-framework-reference-mandatory)
10. [Stage 4 Decision Checklist](#10-stage-4-decision-checklist)
11. [What Stage 5 Does With These Decisions](#what-stage-5-does-with-these-decisions)

---

## 1. MAUI Theming Philosophy

**Input:** MAUI project detected in Stage 2

**Decision Point:** MAUI theming is managed through **SyncfusionThemeResourceDictionary** and MAUI's ResourceDictionary system. Understand your theming strategy:

### MAUI Theming Approaches

**Material Design Themes (Primary) - RECOMMENDED**
- Philosophy: Material Design 3 system with comprehensive color, spacing, and typography rules
- Themes Available: MaterialLight, MaterialDark
- Design implication: Define primary, secondary, tertiary colors; system derives all other colors
- Implementation: `SyncfusionThemeResourceDictionary` with VisualTheme property
- Trade-off: Highly structured, ensures visual cohesion across all Syncfusion MAUI controls

**Cupertino Themes (Apple-Focused)**
- Philosophy: Apple iOS/macOS design language for native-like appearance
- Themes Available: CupertinoLight, CupertinoDark
- Design implication: Respects iOS Human Interface Guidelines
- Implementation: `SyncfusionThemeResourceDictionary` with VisualTheme="CupertinoLight/CupertinoDark"
- Trade-off: Best for iOS/macOS apps, may feel unfamiliar on Android/Windows

**Custom ResourceDictionary (Advanced)**
- Philosophy: Full control over theming by overriding theme keys
- Design implication: You override specific color keys on top of built-in themes
- Implementation: Merged ResourceDictionary with custom key overrides
- Trade-off: Maximum flexibility, requires understanding theme key naming conventions

**Platform-Specific Theming (Cross-Platform)**
- Philosophy: Different themes per platform using x:OnPlatform
- Design implication: iOS uses Cupertino, Android uses Material, Windows uses Material, macOS uses Cupertino
- Implementation: Use `OnPlatform` in App.xaml with platform-specific theme dictionaries
- Trade-off: Better UX per platform, more complex theming code

### The MAUI Best Practice

**Lock to Material Design for cross-platform projects.** MAUI and Syncfusion MAUI controls are built around Material Design 3. All built-in theming, Syncfusion controls, and platform defaults assume Material 3 principles. Use Cupertino only for iOS-focused applications.

### Decision: Confirm Your Theming Strategy

Choose one:
- **MaterialLight/MaterialDark:** Modern, cross-platform, recommended for most projects
- **CupertinoLight/CupertinoDark:** iOS/macOS focused, native Apple look and feel
- **Platform-Specific x:OnPlatform:** Different themes per platform (advanced)
- **Custom Theme Overrides:** Base theme + custom ResourceDictionary overrides (advanced)

**→ MANDATORY:** Your theming strategy is locked. This determines your **Section 9 reference implementation**.

**Output:** MAUI theming strategy understood and confirmed

---

## 2. Syncfusion MAUI Theme Alignment

**Core Principle:** Syncfusion MAUI provides built-in theme support through `SyncfusionThemeResourceDictionary`. All Syncfusion MAUI controls automatically use the selected theme without per-control configuration.

### Syncfusion MAUI Built-In Themes

Syncfusion .NET MAUI includes four built-in themes with automatic merging of all control-specific styles:

**Material Design Themes (Cross-Platform)**
- **MaterialLight** - Material Design 3 light theme (default)
- **MaterialDark** - Material Design 3 dark theme
- Best for: Cross-platform apps, Android, Windows targeting
- Features: Modern design language, comprehensive color palette, automatic dark mode support

**Cupertino Themes (Apple-Focused)**
- **CupertinoLight** - Apple iOS/macOS light theme
- **CupertinoDark** - Apple iOS/macOS dark theme
- Best for: iOS/macOS apps, native Apple experience
- Features: iOS Human Interface Guidelines compliance, natural Apple appearance

### Why SyncfusionThemeResourceDictionary Matters

**Automatic Merging:**
- Single theme declaration = all Syncfusion MAUI controls styled consistently
- No need to manually import individual component themes
- Control-specific styles automatically included

**Example of alignment:**
- MAUI app + MaterialLight = SfDataGrid, SfChart, SfButton all follow Material Design 3
- Consistent spacing, elevation, color system across all controls
- Platform conventions respected automatically

**Example of what NOT to do:**
- Mixing themes (Material 3 controls + Cupertino app) creates inconsistency
- Manual per-control styling defeats automatic merging benefits
- Results in maintenance nightmare and visual inconsistency

### Theme Selection Decision Tree

**If building cross-platform app (Android + iOS + Windows):**
→ Use **MaterialLight/MaterialDark** Syncfusion MAUI theme
- Why: Material Design 3 is cross-platform standard
- Automatic platform respect (Android uses Material natively, iOS adapts well)
- Best developer experience and user consistency

**If building iOS/macOS focused app:**
→ Use **CupertinoLight/CupertinoDark** Syncfusion MAUI theme
- Why: Native Apple look and feel
- Respects iOS Human Interface Guidelines
- Optimal UX for Apple platform users

**If using Platform-Specific Theming:**
→ Use `OnPlatform` with platform-specific theme selection
```xml
<OnPlatform x:TypeArguments="ResourceDictionary">
    <On Platform="iOS, MacCatalyst">
        <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="CupertinoLight"/>
    </On>
    <On Platform="Android, WinUI">
        <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
    </On>
</OnPlatform>
```

**If customizing colors:**
→ Use theme overrides with custom ResourceDictionary
- Base your overrides on Material or Cupertino theme
- Override specific theme keys for branding
- Maintains automatic merging benefits

### The MAUI Best Practice

**Lock to MaterialLight/MaterialDark for all standard projects.** Reasons:
- Syncfusion MAUI controls optimized for Material 3
- Best cross-platform consistency
- Automatic platform adaptation
- Easiest maintenance path

Only use Cupertino if:
- Targeting iOS/macOS as primary platforms
- Requiring native Apple look and feel
- Team familiar with Apple design language

**→ REQUIRED:** Your Syncfusion MAUI theme choice is locked. This determines your **Section 9 reference implementation**.

**📖 For detailed theme implementation:**
Refer to **Skill: syncfusion-maui-theming** → **Applying Themes** for:
- Setting up SyncfusionThemeResourceDictionary in App.xaml
- Platform-specific theme configuration with OnPlatform
- Runtime theme switching for light/dark modes
- Custom color overrides and branding

**Output:** Syncfusion MAUI theme strategy selected and aligned

---

## 3. Color System Architecture

### 3.1 Color Space Decision

**Use OKLCH instead.** It's perceptually uniform: equal steps in lightness *look* equal regardless of hue. This is critical for accessible contrast and visual hierarchy.

**Compatibility Note:** OKLCH works in all modern browsers. For older browser support, generate fallback hex values, but design in OKLCH.

### 3.2 Brand Color & Semantic Palette

**The Decision Point:** Your color palette must have clear roles.

Define:
1. **Primary color** (brand, CTAs, key actions)
2. **Semantic colors** (success, warning, error, info)
3. **Neutral scale** (text, backgrounds, borders)
4. **Surface colors** (cards, modals, containers) — optional if neutrals are sufficient

**MAUI-Specific Consideration:**

- **Material 3 users:** Use Material Design color token naming (primary, secondary, tertiary, error) with fixed roles. Don't deviate.
- **Custom ResourceDictionary:** Define semantic color names (primaryColor, secondaryColor, successColor, etc.) and use them consistently.
- **Platform-Specific:** Use x:OnPlatform for platform-specific color overrides (iOS conventions, Android Material 3, etc.)

**The MAUI Best Practice:** Respect Material 3 semantic system. Your custom primary color might be beautiful, but if it breaks Material 3 semantics, it creates inconsistency across all Syncfusion MAUI controls.

### 3.3 Tinted Neutrals for Cohesion

**Why tinted neutrals matter:**

Pure gray (zero chroma) feels lifeless next to a brand color. Adding a *tiny* chroma value (0.005-0.015) tinted toward your brand hue creates subconscious visual cohesion without reading as "tinted."

**Example:**
- Brand color: oklch(55% 0.12 200) [teal]
- Pure gray: oklch(50% 0 0) [lifeless against teal]
- Tinted gray: oklch(50% 0.01 200) [cohesive, still looks gray]

**But avoid the reflex:** Don't always tint toward warm orange or cool blue. That's the laziest default. Tint toward *this specific project's* brand hue.

### 3.4 Dark Mode: Structural, Not Inverted

**The Misconception:** Dark mode is just light mode inverted.

**The Reality:** Dark mode requires different design thinking:
- Light mode uses shadow for depth
- Dark mode uses lighter surfaces for depth (no shadows)
- Light mode uses vibrant accents
- Dark mode desaturates accents slightly (vibrant colors feel aggressive on dark)
- Light mode: text is dark on light
- Dark mode: text is light on dark, usually needs slightly reduced font weight

**MAUI Consideration:**
- Material 3 uses AppTheme for light/dark mode switching
- MAUI provides built-in dark/light theme support
- Define custom colors in ResourceDictionary with AppThemeBinding

**Decision Point:** Are you supporting dark mode?
- If **no**: You're done here
- If **yes**: Plan it now. It's not an afterthought, it's a design system choice

**📖 For MAUI Dark Mode:**
Refer to **Skill: MAUI Theming** → **Dark Mode Implementation** for:
- Enabling AppTheme switching globally in MauiProgram.cs
- Per-control dark mode customization with AppThemeBinding
- Runtime theme switching implementation (toggles, checkboxes)
- Dark mode behavior for all Syncfusion MAUI controls

**Output:** Color system architecture decided

---

## 4. Spacing & Typography Systems

### 4.1 Spacing Grid: MAUI Standards

**Decision Point:** MAUI uses device-independent units (dp). Respect Material Design spacing scale.

**Material Design 3 Default:** 4dp increments (4dp, 8dp, 12dp, 16dp, 20dp, 24dp...)
- Why: Matches Material Design 3 standards and MAUI platform conventions
- When to override: Only if brand design system requires specific spacing (document it)

**MAUI Implementation:** Use Spacing properties on layouts
- **StackLayout/FlexLayout:** Spacing property (gap between children)
- **Grid:** ColumnSpacing and RowSpacing properties
- **Padding/Margin:** ThicknessResource or direct values
- Define in ResourceDictionary (XAML) for consistency

**Example Spacing ResourceDictionary:**
```xml
<x:Double x:Key="SpaceXXSmall">4</x:Double>
<x:Double x:Key="SpaceXSmall">8</x:Double>
<x:Double x:Key="SpaceSmall">12</x:Double>
<x:Double x:Key="SpaceMedium">16</x:Double>
<x:Double x:Key="SpaceLarge">20</x:Double>
<x:Double x:Key="SpaceXLarge">24</x:Double>
<x:Double x:Key="SpaceXXLarge">32</x:Double>
```

**MAUI Best Practice:** Lock to Material Design 4dp grid
- Why: Syncfusion MAUI controls and Material Design 3 specify this
- When to override: Only if documented strong design system requirements

### 4.2 Typography Hierarchy: Modular Scale, Not Random

**Non-Obvious Principle:** Too many font sizes that are too close together create muddy hierarchy.

Compare:
- ❌ Muddy: 14sp, 15sp, 16sp, 18sp, 20sp (hard to distinguish hierarchy)
- ✅ Clear: 12sp, 16sp, 20sp, 28sp, 36sp (obvious visual progression)

**Use a consistent ratio.** Common options:
- 1.25 (major third) — good balance, subtle but clear
- 1.33 (perfect fourth) — more contrast
- 1.5 (perfect fifth) — high contrast, for designs where hierarchy needs obvious

**MAUI Typography Tiers (Material Design 3):**
- **Display Large:** 57sp (headlines, page titles)
- **Display Medium:** 45sp (major sections)
- **Display Small:** 36sp (secondary headings)
- **Headline Large:** 32sp (primary headers)
- **Headline Medium:** 28sp (section headers)
- **Headline Small:** 24sp (subsections)
- **Body Large:** 16sp (body text, default)
- **Body Medium:** 14sp (secondary body)
- **Body Small:** 12sp (captions, fine print)

**Minimum Body Text Size:** Never smaller than 12sp on mobile (WCAG accessibility requirement).

**Line Height Rule:** 
- Body text: 1.5 (better readability on small screens)
- Headlines: 1.2-1.3 (tight, professional)
- Light text on dark: Add 0.1 to body ratio (light text reads heavier)

**MAUI Implementation:** Define in ResourceDictionary
```xml
<x:Double x:Key="FontSizeHeadingLarge">32</x:Double>
<x:Double x:Key="FontSizeHeadingMedium">28</x:Double>
<x:Double x:Key="FontSizeBodyLarge">16</x:Double>
<x:Double x:Key="FontSizeBodyRegular">14</x:Double>
<x:Double x:Key="FontSizeCaption">12</x:Double>
```

**Decision Point:** Are you using Material Design 3 typography tiers, or defining custom sizes?
- Material 3: Automatic Syncfusion control alignment, proven hierarchy
- Custom: More flexibility, but requires careful testing for hierarchy clarity

**Output:** Spacing and typography systems decided

---

## 5. Responsive Strategy

### 5.1 Mobile-First Thinking

**Principle:** Start with mobile constraints, scale UP with breakpoints. Never design desktop-first and shrink.

Why: Mobile constraints force clarity. Desktop flexibility comes naturally. If you design desktop-first, you'll add unnecessary complexity that breaks on mobile.

### 5.2 Breakpoint Decision

**Standard breakpoints (mobile-first):**
- **Base (0px):** Mobile-optimized layout
- **Tablet (768px):** Content begins expanding
- **Desktop (1024px):** Full layout capability
- **Large (1280px+):** Multi-column, spacious layouts

**Framework Conventions:**
- **Tailwind:** Uses `sm`, `md`, `lg`, `xl` breakpoints; these map to the above
- **Bootstrap:** Uses `xs`, `sm`, `md`, `lg`, `xl`; similar logic
- **Material:** No built-in responsive framework; design with Material's guidelines in mind

**Content-Driven Overrides:** If your content needs a breakpoint at 600px or 900px, add it. Framework defaults are starting points, not absolutes.

**Decision Point:** Are you using your framework's default breakpoints?
- Default: Proven, consistent with framework ecosystem
- Custom: Only if content requires it; document the reasoning

### 5.3 Container Queries vs Viewport Queries

**Viewport queries** (`@media min-width`) work for page-level layouts. **Container queries** work for components that need to respond to their container width, not viewport.

**Example:**
- A card in a narrow sidebar should be compact
- The same card in a main content area should be spacious
- Container queries handle this automatically; viewport queries can't

**Modern best practice:** Use container queries for component layouts, viewport queries for page layouts.

**Compatibility Note:** Container queries are modern (90%+ browser support). If you need older browser support, viewport queries are your fallback.

---

### 5.4 Viewport Coverage: Quick Reference

**CSS Reset (top-level):**
```css
html, body {
  width: 100%;
  height: 100%;
  margin: 0;
  padding: 0;
}

#root {
  width: 100%;
  height: 100%;
}
```

**Full-Height Container (standard web app):**
```css
.container {
  width: 100%;
  min-height: 100vh;  /* NOT height: 100vh */
  padding: var(--space-sm);
}
```

**Mobile Web App (PWA with notches):**
```css
.full-screen {
  width: 100%;
  min-height: 100dvh;
  padding-top: max(var(--space-sm), env(safe-area-inset-top));
  padding-bottom: max(var(--space-sm), env(safe-area-inset-bottom));
}
```

**Key Rules:**
- Use `width: 100%`, NOT `100vw` (avoids scrollbar issues)
- Use `min-height: 100vh`, NOT `height: 100vh` (allows content expansion)
- Use `100dvh` on mobile (dynamic viewport height, accounts for address bars)

**Output:** Responsive strategy decided

---

## 6. Motion & Accessibility Standards

### 6.1 Motion Purpose and Timing

**Rule:** Animations serve specific purposes. Don't animate for aesthetics alone.

Good uses:
- **Transitions:** State changes (button press, hover)
- **Reveals:** Elements appearing (dropdown open, toast notification)
- **Feedback:** User actions acknowledged (loading spinner, success checkmark)

Bad uses:
- Decorative floating elements
- Parallax scrolling
- Anything that doesn't communicate intent

**Standard durations:**
- Micro (100ms): Hover feedback, immediate response
- Standard (300ms): Transitions, state changes, small reveals
- Slow (500ms): Major layout changes, important reveals

**The Non-Obvious Truth:** Slower isn't always better. 300ms feels responsive. 500ms feels sluggish. 100ms feels snappy but can feel jarring on slower devices.

### 6.2 Reduced Motion: Non-Negotiable

**WCAG Requirement:** Respect `prefers-reduced-motion: reduce` by removing animations.

This isn't optional accessibility—it's a legal requirement for accessible UI. Users with vestibular disorders experience motion sickness from animations.

**Implementation:** When a user has `prefers-reduced-motion` enabled, disable all animations (duration → 0ms).

### 6.3 Touch Targets

**Rule:** Interactive elements must be at least 44x44px (WCAG recommendation).

This includes:
- Buttons
- Form inputs
- Links
- Checkbox/radio areas

**Space them at least 8px apart** to prevent accidental touches.

**Visual vs Touch Size:** A button might *look* like 24x24px (visual icon), but its touch target should be 44x44px via padding or pseudo-elements.

### 6.4 Color Contrast

**WCAG 2.1 AA requirement:** 4.5:1 minimum contrast for text and UI components.

This means:
- Dark text on light background must be dark enough
- Light text on dark background must be light enough
- Placeholder text counts—it needs contrast too

**Common fail:** Light gray placeholder text on white. It looks good but fails accessibility.

**Testing:** Don't trust your eyes. Use WCAG contrast checkers to verify.

**Decision Point:** Are you aiming for AA (minimum legal requirement) or AAA (higher standard, harder to achieve)?

**Output:** Motion & accessibility standards understood

---

## 7. Design System Token Architecture

### 7.1 Token Naming: Semantic, Not Descriptive

**The Problem with Descriptive Names:**
- `--blue-600`, `--padding-16` are hardcoded to specific values
- If you need to change "blue" to "purple," you rename every token and break meaning
- New team members don't understand *why* a color is used

**Semantic Naming:**
- `--color-primary` means "brand color" (value irrelevant)
- `--space-lg` means "large spacing" in context
- `--font-heading` means "heading typography" (specific size irrelevant)

If you need to rebrand from blue to purple: change the value once, everywhere understands the intent.

### 7.2 Token Hierarchy (Levels)

**Level 1: Primitive Tokens**
- Base colors, spacing units, font sizes
- Framework-specific (Tailwind defines these; Bootstrap defines these; you define these if greenfield)
- Example: `--color-primary`, `--space-4px`, `--font-base`

**Level 2: Semantic Tokens**
- Composed from primitives, role-based
- `--color-text: var(--color-primary-900)` (semantic: "text should be dark primary")
- `--space-component-gap: var(--space-lg)` (semantic: "components space by large units")
- `--transition-standard: var(--duration-normal)` (semantic: "standard state changes take normal duration")

**Level 3: Component Tokens (Optional)**
- Highly specific to components
- Example: `--button-padding: var(--space-sm)` (button-specific override)
- Only create if you have many component-specific values

**Why This Hierarchy Matters:**
- Primitives stay stable (platform-specific)
- Semantics stay stable (intent-based, survives design changes)
- Component tokens are rare and explicit

### 7.3 Where Tokens Live

**Your framework determines storage:**

- **Tailwind:** `tailwind.config.js` (extends or replaces defaults)
- **Bootstrap:** SCSS variables (often `_variables.scss` before importing Bootstrap)
- **Material:** Material Design tokens (following Material 3 spec)
- **Greenfield:** `styles.css` in your styles file

**Decision Point:** Are you using your framework's native token system or creating custom tokens?
- Native: Easier, matches framework ecosystem
- Custom: More control, but requires discipline to maintain

**Output:** Token architecture understood

---

## 8. Syncfusion MAUI Component Integration

### 8.1 Theme Registration with SyncfusionThemeResourceDictionary

**Principle:** Your Syncfusion MAUI theme is registered in `App.xaml` using `SyncfusionThemeResourceDictionary` with automatic merging of all component styles.

This ensures:
- All Syncfusion MAUI controls use consistent theme colors
- Control-specific styles automatically included (no per-component imports needed)
- Styling cascade works properly
- Consistent appearance across all platforms

**Setup in App.xaml:**

```xml
<Application 
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:syncTheme="clr-namespace:Syncfusion.Maui.Themes;assembly=Syncfusion.Maui.Core"
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

**Platform-Specific Theme Selection:**

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <OnPlatform x:TypeArguments="ResourceDictionary">
                <!-- iOS/macOS: Cupertino theme -->
                <On Platform="iOS, MacCatalyst">
                    <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="CupertinoLight"/>
                </On>
                
                <!-- Android: Material theme -->
                <On Platform="Android">
                    <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
                </On>
                
                <!-- Windows: Material theme -->
                <On Platform="WinUI">
                    <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
                </On>
            </OnPlatform>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

**Decision Point:** Your Syncfusion MAUI theme is now locked. Document which theme you chose and why (should match section 2 decision).

**📖 For Detailed Theme Setup:**
Refer to **Skill: syncfusion-maui-theming** → **Applying Themes** for:
- Complete App.xaml setup with all necessary namespaces
- Platform-specific theme configuration
- Available theme options (MaterialLight, MaterialDark, CupertinoLight, CupertinoDark)
- VisualTheme property reference
- Common setup mistakes and troubleshooting

### 8.2 Runtime Theme Switching

**MAUI apps often support light/dark mode toggling.** Syncfusion MAUI themes support runtime switching without app restart.

**Example: Toggle Dark Mode**

```csharp
private void SwitchTheme(string newTheme)
{
    var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
    
    // Find and remove existing Syncfusion theme
    var existingTheme = mergedDictionaries
        .OfType<SyncfusionThemeResourceDictionary>()
        .FirstOrDefault();
    
    if (existingTheme != null)
    {
        mergedDictionaries.Remove(existingTheme);
    }
    
    // Add new theme
    var newThemeDict = new SyncfusionThemeResourceDictionary
    {
        VisualTheme = newTheme  // "MaterialLight", "MaterialDark", etc.
    };
    mergedDictionaries.Add(newThemeDict);
}

// Usage
private void OnDarkModeToggled(object sender, EventArgs e)
{
    SwitchTheme("MaterialDark");
}
```

**📖 For Theme Switching Implementation:**
Refer to **Skill: syncfusion-maui-theming** → **Theme Switching** for:
- ViewModel pattern for theme management
- ThemeService implementation
- User preference persistence
- Syncing with system/OS theme
- Runtime theme switching edge cases

### 8.3 Custom Color Overrides

**If branding requires custom colors**, override theme keys in your own ResourceDictionary instead of modifying Syncfusion theme directly.

**Example: Custom Primary Color**

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <syncTheme:SyncfusionThemeResourceDictionary VisualTheme="MaterialLight"/>
        </ResourceDictionary.MergedDictionaries>
        
        <!-- Your custom color overrides -->
        <Color x:Key="SfPrimaryBrush">#FF007AFF</Color>  <!-- Custom blue -->
        <Color x:Key="SfAccentBrush">#FFFF3B30</Color>   <!-- Custom red -->
    </ResourceDictionary>
</Application.Resources>
```

**📖 For Color Customization:**
Refer to **Skill: syncfusion-maui-theming** → **Overriding Themes** for:
- Theme key naming conventions (Sf{ControlName}{Element}{Property})
- Common control keys by category
- Selective customization strategies
- Complete theme key reference

### 8.4 Custom ResourceDictionary Coordination

**Non-Obvious Pattern:** Don't override Syncfusion component colors scattered throughout XAML. Instead:

1. Define your custom color keys in ResourceDictionary at app level
2. Syncfusion MAUI theme provides base styling
3. Your custom keys override theme keys globally

**Example thinking:**
- ❌ Override SfDataGrid colors in individual page XAML (scattered, hard to maintain)
- ✅ Override SfDataGrid theme keys in App.xaml ResourceDictionary (centralized, easy to update)

This keeps styling coordinated, maintainable, and respects theming hierarchy.

**MAUI Best Practice:**
- Use SyncfusionThemeResourceDictionary as your base (MaterialLight/MaterialDark/Cupertino)
- Layer custom overrides in the same App.xaml ResourceDictionary
- Never modify Syncfusion theme files directly
- Document all custom theme keys

**📖 For Theme Key Customization:**
Refer to **Skill: syncfusion-maui-theming** → **Overriding Themes** and **Creating Custom Themes** for:
- Theme key naming conventions (Sf{ControlName}{Element}{Property})
- Control-specific vs global color keys
- Merged dictionary layer order
- Creating completely custom themes from scratch
- Corporate branding theme examples
- High contrast accessibility themes

**Output:** Syncfusion MAUI integration strategy decided

---

## 9. Load Your Framework Reference (MANDATORY)

**REQUIRED STEP:** Your MAUI theming strategy from Sections 1-2 now determines your implementation guide.

### MAUI Implementation Reference

Based on your **MAUI Theming Philosophy** selection in Section 1 and **Syncfusion MAUI Theme** selection in Section 2, your implementation strategy is automatically locked:

#### If You Selected: **Material Design Theme (Cross-Platform)**
→ **Load Reference:** **Skill: syncfusion-maui-theming** → **Applying Themes**

**What this reference provides:**
- Material Design 3 system with Syncfusion MaterialLight/MaterialDark themes
- SyncfusionThemeResourceDictionary setup in App.xaml
- MAUI spacing system (4dp Material grid)
- MAUI typography hierarchy (Material 3 tiers)
- Color architecture aligned with Material Design 3
- Platform-agnostic implementation (Android, iOS, Windows, macOS)
- Responsive layout with MAUI safe area handling
- Accessibility standards for Material Design
- Stage 5 code generation patterns for Material MAUI projects

**Key principle:** Material Design 3 is prescriptive. You'll follow Material's spacing scale (4dp multiples), typography system (Display, Headline, Body, Label), and color token patterns. Syncfusion's MaterialLight/MaterialDark theme handles all component styling automatically. All platforms get consistent Material experience.

---

#### If You Selected: **Cupertino Theme (Apple-Focused)**
→ **Load Reference:** **Skill: syncfusion-maui-theming** → **Applying Themes**

**What this reference provides:**
- Apple iOS/macOS design with Syncfusion CupertinoLight/CupertinoDark themes
- SyncfusionThemeResourceDictionary setup with Cupertino visual theme
- Apple-specific spacing and typography conventions
- iOS Human Interface Guidelines alignment
- macOS design patterns integration
- Safe area handling for iPhone notches/Dynamic Island
- Platform optimization (iOS/MacCatalyst use Cupertino natively)
- Accessibility standards for Cupertino design
- Stage 5 code generation patterns for Cupertino MAUI projects

**Key principle:** Cupertino is Apple-centric. Best for iOS/macOS apps. Android/Windows will adapt to Apple conventions (not ideal for those platforms). Use Material if you need cross-platform consistency.

---

#### If You Selected: **Platform-Specific Themes**
→ **Load Reference:** **Skill: syncfusion-maui-theming** → **Applying Themes** (Platform-Specific Section)

**What this reference provides:**
- Conditional theme application using `OnPlatform` in App.xaml
- iOS/MacCatalyst → CupertinoLight/CupertinoDark
- Android/Windows → MaterialLight/MaterialDark
- Platform-native design language per target OS
- Safe area and platform-specific layout considerations
- Per-platform typography and spacing adjustments
- Runtime platform detection patterns
- Stage 5 code generation for platform-specific MAUI projects

**Key principle:** Each platform gets its native design language. Maximum UX polish per platform, maximum code complexity. Recommended for apps with premium positioning and platform expertise.

---

#### If You Customizing Colors (Branding)
→ **Load Reference:** **Skill: syncfusion-maui-theming** → **Overriding Themes**

**What this reference provides:**
- Custom color overrides on base themes (Material or Cupertino)
- Theme key naming conventions (Sf{ControlName}{Element}{Property})
- ResourceDictionary layer structure for color customization
- Corporate branding implementation
- Custom accent/primary color setup
- Dark mode variants for custom colors
- Complete theme key reference for all Syncfusion MAUI controls
- Stage 5 patterns for maintaining custom themes

**Key principle:** Base your custom colors on a Syncfusion MAUI theme (Material or Cupertino), then override specific keys. Don't create themes from scratch unless absolutely necessary.

---

#### If You Need Dynamic Theme Switching
→ **Load Reference:** **Skill: syncfusion-maui-theming** → **Theme Switching**

**What this reference provides:**
- Runtime theme switching (no app restart)
- Light/dark mode toggles
- ThemeService implementation pattern
- ViewModel binding for theme settings
- User preference persistence
- System theme synchronization
- Navigation pattern for theme switching UI
- Common edge cases and troubleshooting

**Key principle:** MAUI allows runtime theme switching. Implement via ThemeService + ViewModel for clean separation of concerns.

---

**You cannot proceed to Stage 5 without reviewing your appropriate Syncfusion MAUI Theming reference.**

**Output:** MAUI theming implementation reference locked based on your Section 1-2 decisions

---

## 10. Stage 4 Decision Checklist

**Load Your MAUI Theming Reference (MANDATORY)**

**Upon completion, confirm the following decisions are locked:**

### Syncfusion MAUI Theme Strategy
- ✅ Syncfusion MAUI theme selected and documented (MaterialLight/MaterialDark/CupertinoLight/CupertinoDark)
- ✅ Theme architecture confirmed (single theme / platform-specific / custom overrides)
- ✅ SyncfusionThemeResourceDictionary setup planned for App.xaml
- ✅ MAUI theming philosophy understood (why this theme for this project)

### Color System
- ✅ Color space decided (OKLCH recommended; Material 3 for Material projects)
- ✅ Primary/semantic colors defined
- ✅ Tinted neutrals strategy understood
- ✅ Dark mode decision made (light only / dark only / both)

### Spacing & Typography
- ✅ Spacing scale confirmed (framework default or custom, with rationale)
- ✅ Typography hierarchy locked (modular scale ratio or framework convention)
- ✅ Line height rules applied (readability standards)

### Responsive Design (MAUI-Specific)
- ✅ Safe area strategy confirmed (iOS notches, Android cutouts)
- ✅ MAUI layout properties understood (Padding, Spacing, ColumnSpacing, RowSpacing)
- ✅ Cross-platform layout testing planned (iOS, Android, Windows, macOS)
- ✅ Platform-specific spacing defined (if using OnPlatform)

### Accessibility
- ✅ Motion standards applied (100ms / 300ms / 500ms durations)
- ✅ Reduced motion support confirmed
- ✅ Touch targets sized (44x44px minimum)
- ✅ Color contrast verified (WCAG AA or AAA goal)

### Token Architecture
- ✅ Token storage location decided:
  - **Material 3 projects:** ResourceDictionary with Material Design tokens
  - **Custom projects:** ResourceDictionary with semantic color tokens
  - **Platform-Specific:** x:OnPlatform with platform-specific values
- ✅ Semantic token naming understood (primaryColor, secondaryColor, etc.)
- ✅ Implementation approach locked:
  - **Material 3:** Use ResourceDictionary + AppThemeBinding for dark mode
  - **Custom:** Define all tokens in ResourceDictionary explicitly

### Syncfusion Integration
- ✅ Theme registration point confirmed (app entry point)
- ✅ Color coordination strategy understood (inherit from tokens, don't override)

### MAUI Reference (MANDATORY)
- ✅ MAUI theming reference file loaded (Material 3 / Custom ResourceDictionary)
- ✅ Implementation guide understood for your theming strategy
- ✅ Ready to proceed to Stage 5 with theming decisions locked

---

## What Stage 5 Does With These Decisions

Stage 5 (Code Generation) uses your Stage 4 decisions to generate:
- **MAUI app setup** with correct Syncfusion MAUI theme imports
- **ResourceDictionary** with color tokens and implicit styles
- **AppTheme configuration** in MauiProgram.cs for light/dark mode
- **Base control styles** following your accessibility standards
- **Platform-specific overrides** using x:OnPlatform where needed
- **Syncfusion control integration** that respects your token system

Stage 5 generates *implementation*, not *decisions*. The decisions you locked in Stage 4 ensure Stage 5 output is consistent and coherent.

---

### For All MAUI Projects:
- ✅ Syncfusion MAUI imports match locked theme (Material 3 primary)
- ✅ WCAG 2.1 AA accessibility (contrast, focus states, touch targets)
- ✅ Cross-platform support verified (iOS, Android, Windows, macOS)
- ✅ C# compilation without errors
- ✅ ResourceDictionary tokens applied consistently
- ✅ Build optimization (CSS purging, code splitting)

**Output:** Production-ready code aligned with Stage 4 design decisions
