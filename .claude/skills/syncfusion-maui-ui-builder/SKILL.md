---
name: syncfusion-maui-ui-builder
description: Generates production-ready .NET MAUI UI components and pages powered by Syncfusion MAUI Controls. Orchestrates 8-stage workflow that handles design thinking, component picking, code generation, and validation with built-in WCAG 2.1 AA accessibility, responsive design, and multi-platform support (iOS, Android, Windows, macOS). Use when the user asks to create MAUI components, build UI pages, design interfaces, or generate frontend code for .NET MAUI applications.
compatibility: .NET MAUI 8.0+, .NET 9.0+, Visual Studio 2022 (v17.8+), VS Code with C# DevKit
metadata:
  author: Syncfusion Inc
  version: "1.0.0"
  supported-frameworks: ".NET MAUI 9.0+, .NET 10.0+"
  supported-platforms: "iOS 14+, Android 10+ (API 29+), macOS 11+, Windows 10+"
  component-library: "Syncfusion MAUI Controls"
---

# Syncfusion MAUI UI Builder

## Overview

The **Syncfusion MAUI UI Builder** skill is a presentation-layer .NET MAUI component generator that orchestrates an AI agent through 8 stages to generate production-ready UI components powered by Syncfusion MAUI Controls. Supports iOS, Android, Windows, and macOS with unified codebase.

This spec is **Agent Skills Specification compliant** with one-level-deep file references for optimal context loading by AI agents.

## What This Skill Does

**✅ Generates:**
- MAUI views (`.xaml`) with Syncfusion control declarations
- Code-behind files (`.xaml.cs`) with event handlers and logic
- ViewModels (`.cs`) with MVVM pattern and INotifyPropertyChanged
- MAUI ResourceDictionary for colors, styles, and design tokens
- Syncfusion MAUI component integration with correct imports and properties
- Form validation logic with data annotation support
- WCAG 2.1 AA accessibility markup (semantic control hierarchy, automation IDs)
- Responsive layouts with FlexLayout and Grid (mobile-first, scales to tablet/desktop)
- Multi-platform support (OnPlatform styling for iOS/Android/Windows/macOS)
- Proper C# typing and MVVM command infrastructure

**❌ Does NOT Generate:**
- Backend code (API endpoints, services, middleware)
- Database schemas or Entity Framework models
- Authentication/authorization logic (services responsibility)
- Business logic or data access layer
- Navigation Shell configuration (Shell.xaml routing)
- Platform-specific native code (Objective-C, Swift, Java, Kotlin)
- Environment secrets or infrastructure config

## When to Use

### ✅ USE this Orchestrator Agent for:

- **Full UI builds** with 3+ Syncfusion controls
- **Design system decisions** required (colors, spacing, typography)
- **Complete pages or dashboards** from scratch
- **WCAG 2.1 AA validation** for complex layouts
- **Multi-stage workflows** requiring design → code → validate
- **Team collaboration** on larger component projects
- Examples:
  - Building a complete MAUI admin dashboard
  - Designing a multi-page wizard interface
  - Creating a full data management portal with multiple sections

### ❌ DO NOT USE this Orchestrator for:

- ✋ Configuring a single control (use skill directly)
- ✋ Quick implementation questions (use skill directly)
- ✋ Component tutorials or how-tos (use skill directly)
- ✋ Troubleshooting component issues (use skill + diagnostic protocol)
- ✋ Backend/API code (out of scope)
- ✋ Non-Syncfusion MAUI questions (use general help)

## Supported Components (Syncfusion MAUI)

- **Forms**: Login (SfTextInputLayout, SfCheckBox), registration, password reset, contact forms, multi-step wizards
- **Data Display**: SfDataGrid (sorting, filtering, pagination), SfListView, SfCharts, SfTreeView, SfKanban
- **Input Controls**: SfTextInputLayout, SfMaskedEntry, SfComboBox, SfDatePicker, SfTimePicker, SfCheckBox, SfRadioButton
- **Navigation**: SfTabView, SfNavigationDrawer, SfToolbar, Breadcrumbs (custom)
- **Common Patterns**: SfPopup (modals/dialogs), SfBadgeView (badges), SfChip (tags), SfCarousel (carousels)
- **Page Templates**: Dashboards, e-commerce catalog, checkout flows, portfolios, user profiles, settings pages
- **Indicators & Feedback**: SfBusyIndicator, SfLinearProgressBar, SfCircularProgressBar, SfLinearGauge

## Skill Structure

```
syncfusion-maui-ui-builder/
├── SKILL.md                                     # This file (Agent Skills spec compliant)
├── references/                                  # One-level-deep stage guides + support docs
│   ├── stage-1-intent-analysis.md              # Stage 1: Intent Analysis
│   ├── stage-2-project-detection.md            # Stage 2: Project Detection
│   ├── stage-3-layout-analysis.md              # Stage 3: Layout Analysis & Component Mapping
│   ├── stage-4-theming-and-design-system.md    # Stage 4: Theming & Design System Selection (MAUI ResourceDictionary)
│   ├── stage-5-code-generation.md              # Stage 5: Code Generation (.xaml, .xaml.cs, ViewModel.cs)
│   ├── stage-6-validation.md                   # Stage 6: Validation (WCAG 2.1 AA + Syncfusion integration)
│   ├── stage-7-dependencies.md                 # Stage 7: Dependencies (NuGet packages)
│   ├── stage-8-code-insertion.md               # Stage 8: Code Insertion into MAUI project
│   ├── LAYOUT-DESIGN.md                        # MAUI design thinking guide (accessibility & responsive patterns)
│   ├── MAUI-DOTNET-STANDARDS.md                # WCAG 2.1 AA + MAUI standards + Syncfusion integration rules
│   └── TROUBLESHOOTING.md                      # MAUI-specific error solutions & FAQ
└── assets/                                     # Static resources
    ├── validation-rules.md                     # Validation checklist for Stage 6
    └── templates/                              # MAUI .xaml and C# templates
```

## Quick Start

### Prerequisites

1. **Active .NET MAUI project** (.NET MAUI 8.0+, multi-platform targets: iOS, Android, Windows, macOS)
2. **.NET 8.0+** or **.NET 9.0+** SDK installed
3. **Visual Studio 2022 (v17.8+)** or **VS Code with C# DevKit**
4. **Syncfusion MAUI components library** (auto-installed during Stage 7):
   ```bash
   dotnet add package Syncfusion.Maui.Core
   dotnet add package Syncfusion.Maui.Inputs
   dotnet add package Syncfusion.Maui.DataGrid
   ```
5. **Syncfusion license key** (Community, Trial, or Commercial) — prompted in Stage 2
   - Get free Community License: https://www.syncfusion.com/account/manage-trials

### Basic Usage

**Example 1: Generate a Login Page**

```
User: "Create a login page with email, password, and remember me checkbox for my MAUI app"

Skill executes:
  → Stage 1: Identifies login form component type
  → Stage 2: Detects MAUI project structure, .NET version, target platforms
  → Stage 3: Maps layout fields to Syncfusion MAUI controls (SfTextInputLayout, SfCheckBox, SfButton)
  → Stage 4: Locks design system (Material theme, MAUI ResourceDictionary, responsive layout)
  → Stage 5: Generates LoginPage.xaml + LoginPage.xaml.cs + LoginPageViewModel.cs with MVVM
  → Stage 6: Validates WCAG 2.1 AA accessibility + Syncfusion integration
  → Stage 7: Detects and installs Syncfusion NuGet packages
  → Stage 8: Inserts code into MAUI project, updates namespaces

Output:
  ✓ Views/LoginPage.xaml (XAML view with Syncfusion controls)
  ✓ Views/LoginPage.xaml.cs (Code-behind with bindings)
  ✓ ViewModels/LoginPageViewModel.cs (MVVM ViewModel with INotifyPropertyChanged)
  ✓ Works on iOS, Android, Windows, macOS with platform-specific styling
```

**Example 2: Generate a Customer Data Grid**

```
User: "Build a customer data table with sorting, filtering, and pagination"

Output:
  ✓ Views/CustomerDataGridPage.xaml (SfDataGrid control with columns)
  ✓ Views/CustomerDataGridPage.xaml.cs (Event handlers)
  ✓ ViewModels/CustomerDataGridViewModel.cs (ObservableCollection with mock data)
  ✓ OnPlatform styling for iOS/Android/Windows
  ✓ Responsive layout adapts to screen sizes
  ✓ WCAG 2.1 AA keyboard navigation and accessibility compliance
```

## How It Works: 8-Stage AI Orchestration (Stateless)

The skill orchestrates **8 stages of pure AI reasoning** with **two user decision points**.

**Key Architecture:**
- **Stateless design**: Conversation history maintains state across stages
- **Pure AI reasoning**: Each stage reads guidance docs, analyzes context, makes MAUI-informed decisions
- **2 user decision gates**: Stage 3 (Syncfusion component confirmation) + Stage 6 (validation result)
- **5 fully automated stages**: 1, 2, 4, 5, and 7-8
- **Dedicated theming stage**: Stage 4 locks MAUI design system (ResourceDictionary, theme, colors, spacing) before code generation
- **Multi-platform support**: All generated code targets iOS, Android, Windows, macOS with OnPlatform adaptation

```
User Request
    ↓
[Stage 1: Intent Analysis] 
  AI reads query → identifies component type, features, target complexity
    ↓
[Stage 2: Project Detection]
  AI scans project → detects .NET version, MAUI target platforms
  Detects MVVM architecture, styling strategy (ResourceDictionary/CSS)
  Reads Syncfusion license preferences
    ↓
[Stage 3: Layout Analysis & Component Mapping] ⭐ USER DECISION #1
  AI analyzes requirements → creates optimal component-mapping.json
  AI maps to specific Syncfusion MAUI controls (SfTextInputLayout, SfDataGrid, etc.)
  Minimum 3+ controls mapped explicitly
  User confirms Syncfusion component selection
    ↓
[Stage 4: Theming & Design System]
  AI locks MAUI styling strategy (ResourceDictionary or XAML Styles)
  AI selects Syncfusion theme (Material/Cupertino via SyncfusionThemeResourceDictionary)
  AI confirms color system (MAUI Color resources, semantic tokens)
  AI confirms spacing scale (4pt MAUI grid, scale factors)
  AI confirms typography hierarchy (modular scale, minimum 16px body)
  Design system + multi-platform (OnPlatform) strategy locked
    ↓
[Stage 5: Code Generation]
  AI generates .xaml, .xaml.cs, ViewModel.cs with MVVM pattern
  Uses theming decisions and Syncfusion components from Stage 4
  Includes INotifyPropertyChanged, ObservableCollection for data binding
  With WCAG 2.1 AA accessibility + responsive layout + multi-platform support
    ↓
[Stage 6: Validation] ⭐ USER DECISION #2
  AI validates WCAG 2.1 AA + Syncfusion integration + MAUI standards + security
  Binary result: PASS ✓ or FAIL ✗
  User confirms or overrides
    ↓
[Stage 7: Dependencies]
  AI scans code for Syncfusion control usings
  Detects required NuGet packages (Syncfusion.Maui.*)
  Checks .csproj for version conflicts
  Generates dotnet add package commands or runs them
    ↓
[Stage 8: Code Insertion]
  AI inserts files into project (Views/, ViewModels/, Resources/)
  Updates project file if needed
  Registers Syncfusion license in MauiProgram.cs
  Verifies dotnet build succeeds
    ↓
✓ Complete - Component ready for all platforms (iOS, Android, Windows, macOS)
```

**Stage Descriptions:**

- **Stage 1 (Intent Analysis)**: Parse user query, identify component type, complexity, and features. Read: `references/stage-1-intent-analysis.md`
- **Stage 2 (Project Detection)**: Auto-detect .NET version, MAUI target platforms (iOS/Android/Windows/macOS), MVVM architecture, styling strategy. Read: `references/stage-2-project-detection.md`
- **Stage 3 (Layout Analysis & Component Mapping)**: Analyze requirements, create optimal component-mapping.json, map to 3+ Syncfusion MAUI controls (SfTextInputLayout, SfDataGrid, etc.). User confirms. Read: `references/stage-3-layout-analysis.md`
- **Stage 4 (Theming & Design System)**: Lock MAUI styling (ResourceDictionary), Syncfusion theme (Material/Cupertino), color system, spacing (4pt grid), typography (modular scale), and OnPlatform strategy. Read: `references/stage-4-theming-and-design-system.md`
- **Stage 5 (Code Generation)**: Generate .xaml, .xaml.cs, ViewModel.cs with MVVM pattern and INotifyPropertyChanged. Apply theming decisions + Syncfusion components + accessibility + responsive design. Read: `references/stage-5-code-generation.md`
- **Stage 6 (Validation)**: Validate WCAG 2.1 AA, Syncfusion integration, MAUI standards, security. Binary pass/fail result. Read: `references/stage-6-validation.md` + `assets/validation-rules.md`
- **Stage 7 (Dependencies)**: Detect Syncfusion.Maui.* NuGet packages needed, resolve version conflicts, generate dotnet add commands. Read: `references/stage-7-dependencies.md`
- **Stage 8 (Code Insertion)**: Insert files (Views/, ViewModels/), register license in MauiProgram.cs, verify dotnet build succeeds.

**User Interaction Summary:**

| Stage | Interaction |
|-------|-------------|
| 1 | None (AI analyzes) |
| 2 | Confirm auto-detected settings (.NET version, target platforms, MVVM pattern) |
| 3 | ⭐ Confirm Syncfusion MAUI component selection (minimum 3+ controls) |
| 4 | Confirm theming decisions (ResourceDictionary strategy, Syncfusion theme, colors, spacing, typography, OnPlatform approach) |
| 5 | None (AI generates) |
| 6 | ⭐ Confirm validation result (PASS ✓ or FAIL ✗, with option to override) |
| 7 | Optional (confirm NuGet package installation via dotnet) |
| 8 | None (AI executes) |

**Total user decision gates: 2** (Stage 3: Syncfusion components, Stage 6: validation). Rest fully automated with AI reasoning + guidance docs. All generated code targets iOS, Android, Windows, and macOS simultaneously.

## Agent Instructions

### When User Requests MAUI UI Component Generation

1. **Validate scope**: Confirm request is for MAUI presentation-layer components (not backend/APIs/Services)
2. **Load guidance**: Read `stage-1-intent-analysis.md` to understand Stage 1
3. **Execute 8-stage flow**: Follow the orchestration flow shown above, enforcing Syncfusion MAUI controls
4. **Progressive disclosure**: Load stage guides on-demand; load support references only when needed
5. **Maintain conversation history**: Each stage reads previous decisions from conversation context (stateless design)
6. **Enforce multi-platform**: All generated code must target iOS, Android, Windows, and macOS

### Stage Execution & Reference Loading

**Stage 1: Intent Analysis**
- Read: `references/stage-1-intent-analysis.md`
- Task: Parse user query, identify component type, complexity level, target features
- Output: Component type + modifiers + MAUI component class recommendations

**Stage 2: Project Detection**
- Read: `references/stage-2-project-detection.md`
- Task: Auto-detect .NET version, MAUI target platforms (iOS/Android/Windows/macOS), MVVM pattern, styling strategy
- Detect: Syncfusion license status, existing MAUI ResourceDictionary configuration
- Output: Project configuration + platform targets + user confirmation

**Stage 3: Layout Analysis & Component Mapping** ⭐ USER DECISION #1
- Read: `references/stage-3-layout-analysis.md`
- Task: Analyze user requirements → create optimal component-mapping.json → map to 3+ Syncfusion MAUI controls explicitly
- Example: SfTextInputLayout for text input, SfDataGrid for tables, SfCheckBox for boolean
- Output: component-mapping.json + Syncfusion control list + User confirmation

**Stage 4: Theming & Design System**
- Read: `references/stage-4-theming-and-design-system.md`
- Task: Lock MAUI styling strategy (ResourceDictionary vs XAML Styles), Syncfusion theme (Material/Cupertino), MAUI Color resources, spacing (4pt grid), typography (modular scale), OnPlatform multi-platform approach
- Output: Design system decisions confirmed, ready for code generation with all platforms targetted

**Stage 5: Code Generation**
- Read: `references/stage-5-code-generation.md`
- Task: Generate .xaml (view), .xaml.cs (code-behind), ViewModel.cs (MVVM with INotifyPropertyChanged) using theming from Stage 4
- Ensure: WCAG 2.1 AA accessibility, responsive layouts (FlexLayout, Grid), OnPlatform styling, Syncfusion component bindings
- Output: Generated files (.xaml, .xaml.cs, ViewModel.cs) ready for review

**Stage 6: Validation** ⭐ USER DECISION #2
- Read: `references/stage-6-validation.md` + `assets/validation-rules.md` + `references/MAUI-DOTNET-STANDARDS.md`
- Task: Validate against WCAG 2.1 AA, Syncfusion MAUI integration, MAUI standards, security, multi-platform support
- Auto-apply fixes where possible (accessibility, naming conventions)
- Output: Binary result (PASS ✓ or FAIL ✗) → user confirms or overrides

**Stage 7: Dependencies**
- Read: `references/stage-7-dependencies.md`
- Task: Scan code for Syncfusion control usings, detect required NuGet packages (Syncfusion.Maui.*), resolve version conflicts in .csproj
- Output: dotnet add package commands or auto-install via dotnet CLI

**Stage 8: Code Insertion**
- Read: `references/stage-8-code-insertion.md`
- Task: Insert files into project (Views/, ViewModels/), register Syncfusion license in MauiProgram.cs, update project file, verify dotnet build succeeds
- Output: Success report with file paths, build verification status



### Boundary Rules (CRITICAL)

**AI agents executing this skill MUST:**

1. **Presentation layer only**: Never generate backend code (API services, database schemas, business logic)
2. **MAUI Views & ViewModels only**: Generate `.xaml`, `.xaml.cs`, and `ViewModel.cs` files in Views/ and ViewModels/ directories
3. **Mock data only**: Use `ObservableCollection` with hardcoded samples; no direct HTTP calls to real APIs
4. **No platform-specific native code**: Use XAML/C# only; don't generate Objective-C, Swift, Java, or Kotlin
5. **No Shell/Navigation setup**: Don't generate Shell.xaml routing; user configures navigation separately
6. **Control selection integrity**: 
   - ✅ Prefer Syncfusion MAUI controls first (SfTextInputLayout, SfDataGrid, SfButton, SfCheckBox, etc.)
   - ✅ If required control is NOT available in Syncfusion, fall back to native MAUI framework controls (Button, Entry, Label, Picker, etc.)
   - ❌ **DO NOT use incorrect or incompatible Syncfusion controls** — verify control exists and supports the use case
   - ❌ **DO NOT add non-existent Syncfusion skills** — consult skill documentation for available controls
7. **Multi-platform required**: All generated code must target iOS, Android, Windows, macOS (use OnPlatform where needed)
8. **File handling libraries out of scope (but can be referenced from UI)**: Word, PDF, Excel, PowerPoint, PDFToImage, Markdown, SmartDataExtraction, Calculate are backend/document processing services — NOT UI components. These must be handled by backend services or separate backend skills. However, UI forms CAN trigger or reference these services for certain needed actions (e.g., export to PDF button, import Excel data button, generate Word document). The services themselves are not generated by this skill — only UI buttons/actions that call them.
9. **Redirect backend/API requests**: *"This skill generates MAUI UI only. Service integration is your app's responsibility. Ready to generate the view with mock data?"*

### Error Handling

If any stage fails:

1. **Retry once** with same approach
2. **If retry fails**, attempt workaround or skip to next stage
3. **Notify user** with error message from stage output
4. **Offer recovery**: *"Would you like to go back to Stage 3 and choose a different layout?"*
5. **Reference**: `references/TROUBLESHOOTING.md` for common errors

### Resource Loading Strategy (Progressive Disclosure)

**Load SKILL.md first** (you're reading it now) ~500 lines for MAUI

**Load stage guides on-demand** (each ~150-250 lines):
- `stage-1-intent-analysis.md` → During Stage 1
- `stage-2-project-detection.md` → During Stage 2 (MAUI .NET version, platform detection)
- `stage-3-layout-analysis.md` → During Stage 3 (Syncfusion component mapping)
- `stage-4-theming-and-design-system.md` → During Stage 4 (ResourceDictionary, OnPlatform)
- `stage-5-code-generation.md` → During Stage 5 (XAML, C#, MVVM generation)
- `stage-6-validation.md` → During Stage 6 (validation checklist)
- `stage-7-dependencies.md` → During Stage 7 (NuGet packages)
- `stage-8-code-insertion.md` → During Stage 8 (file insertion, build verification)

**Load support references only when needed**:
- `LAYOUT-DESIGN.md` → For MAUI-specific design patterns and accessibility
- `MAUI-DOTNET-STANDARDS.md` → When validating or checking best practices
- `TROUBLESHOOTING.md` → When errors occur
- `assets/validation-rules.md` → When validating in Stage 6

**Result**: Initial load ~500 lines (SKILL.md only). Full spec available on-demand, never exceeding Agent Skills context limits.

## Configuration & User Customization

### Auto-Detected Settings

During **Stage 2 (Project Detection)**, AI automatically detects:

- **.NET Version**: .NET 8.0, .NET 9.0+
- **MAUI Target Platforms**: iOS, Android, Windows, macOS (from .csproj TargetFrameworks)
- **Architecture Pattern**: MVVM (default) or code-behind
- **Styling Strategy**: MAUI ResourceDictionary (default) or XAML Styles
- **Project Structure**: Views/, ViewModels/, Resources/ directory locations
- **Formatting**: C# conventions (indentation, naming, braces)
- **Syncfusion License Status**: Registered or trial/community

### User Override Options

In **Stage 2**, user can override any detected setting:

```
Detected Settings:
  .NET Version: 9.0
  Target Platforms: iOS, Android, Windows, macOS
  Architecture: MVVM
  Styling: ResourceDictionary
  Syncfusion License: Community (valid)

[Confirm] [Override Each] [Cancel]
```

### Syncfusion License Configuration

The skill handles Syncfusion license key setup:

1. **Check** for existing license in MauiProgram.cs or environment
2. **If missing**, prompt user: *"Get a free Community License at https://www.syncfusion.com/account/manage-trials"*
3. **If provided**, inject `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licenseKey)` in MauiProgram.cs
4. **If skipped**, proceed but warn that Syncfusion controls will show license watermark in debug/release

---

## Code Generation Standards

All generated code includes:

### Accessibility (WCAG 2.1 AA)
- ✅ Semantic MAUI control hierarchy (semantic controls for roles)
- ✅ Automation IDs for all interactive elements
- ✅ Accessible labels and descriptions (SemanticProperties)
- ✅ Keyboard navigation (TabIndex, focus order management)
- ✅ Color contrast ≥ 4.5:1 (verified via MAUI Color resources)
- ✅ Touch-friendly targets (44x44 points minimum per MAUI guidance)
- ✅ Reduced motion support (respects device accessibility settings)

### Responsive Design (Multi-Platform)
- ✅ Mobile-first layout (320pt base, scales to tablet/desktop)
- ✅ FlexLayout or Grid (no fixed widths, adaptable to screen size)
- ✅ OnPlatform styling (platform-specific adjustments for iOS/Android/Windows/macOS)
- ✅ Safe area handling (iOS notches, Android cutouts via MAUI Margins)
- ✅ Breakpoints for mobile (320-480pt), tablet (480-1024pt), desktop (1024pt+)

### Security
- ✅ Input validation with data annotations
- ✅ No hardcoded secrets or API keys
- ✅ Proper null handling and exception management
- ✅ No direct SQL or unvalidated data access
- ✅ Secure Syncfusion license key management (not exposed in XAML)

### Performance (MAUI Optimized)
- ✅ INotifyPropertyChanged for efficient data binding
- ✅ ObservableCollection for efficient list updates
- ✅ UI virtualization for large lists (if applicable)
- ✅ Lazy loading for images and heavy components
- ✅ Appropriate use of async/await for background tasks

### C# & MAUI Best Practices
- ✅ Full type safety (no `dynamic` or untyped objects)
- ✅ MVVM pattern with ViewModel and ICommand
- ✅ Proper XAML namespaces for Syncfusion controls
- ✅ XML documentation comments on public members
- ✅ C# 11+ modern language features (nullable reference types, records where appropriate)

## Supported Use Cases (Syncfusion MAUI)

- **Login page**: SfTextInputLayout (email, password), SfCheckBox (remember me), SfButton (submit) + validation
- **Customer data grid**: SfDataGrid with sorting, filtering, pagination, row selection, multi-select
- **Dashboard**: Multiple Syncfusion components (SfTabView for tabs, SfDataGrid for data, SfCharts for visualizations)
- **Registration wizard**: Multi-step form with SfSegmentedControl (steps), validation per step, SfButton (next/submit)
- **Settings page**: SfSwitch (toggles), SfComboBox (dropdowns), SfTextInputLayout (text fields), SfDatePicker (date selection)
- **E-commerce catalog**: SfCarousel (product showcase), SfDataGrid or SfListView (product list), SfRating (reviews)
- **Employee list**: SfListView with pull-to-refresh, SfSearchBar, individual detail views with SfDataForm

## Troubleshooting

**Common Issues:**

| Issue | Solution |
|-------|----------|
| "Project type not detected" | Ensure `.csproj` file exists with MAUI TargetFrameworks |
| "No .NET 8+ SDK found" | Install .NET 8.0 or 9.0+ from https://dotnet.microsoft.com/download |
| "Syncfusion controls show watermark" | Register valid Community/Trial/Commercial license via Stage 2 |
| "Build fails after file insertion" | Check namespace conflicts; verify Syncfusion NuGet versions match |
| "View not rendering on iOS/Android" | Verify OnPlatform styling is correct; check platform-specific control availability |
| "Compilation error: 'SfXXX' not found" | Ensure correct `xmlns:syncfusion="clr-namespace:Syncfusion.Maui.XYZ"` namespace in XAML |
| "WCAG validation fails" | Check automation IDs, semantic control hierarchy, color contrast ratios |

**Full guide**: See `references/TROUBLESHOOTING.md`

## Additional Resources

### Quick Reference by Use Case

| Need | Reference File |
|------|-----------------|
| Understanding workflow | This SKILL.md file |
| How Stage X works | `references/stage-X-*.md` |
| MAUI-specific design patterns | `references/LAYOUT-DESIGN.md` |
| Validation rules and checklist | `assets/validation-rules.md` |
| WCAG 2.1 AA + MAUI standards | `references/MAUI-DOTNET-STANDARDS.md` |
| Troubleshooting MAUI errors | `references/TROUBLESHOOTING.md` |
| Syncfusion MAUI control reference | `syncfusion-maui-theming`, `syncfusion-maui-buttons`, etc. (skills library) |

### Architecture Compliance

✅ **Agent Skills Specification Compliant v2.0**
- YAML frontmatter with MAUI metadata (.NET versions, platforms)
- One-level-deep file structure (no nested references between guides)
- Progressive disclosure (core ~500 lines, details on-demand)
- Markdown-based guidance (AI-native format)
- Clear scope boundaries (MAUI presentation layer only)
- Multi-platform support (iOS, Android, Windows, macOS)

## License & Attribution

- **UI Builder Skill for MAUI**: Proprietary (Syncfusion)
- **Syncfusion MAUI Components**: Requires valid Syncfusion license (Community/Free, Trial, or Commercial)
- **Official Components Repository**: https://github.com/syncfusion/maui-ui-components-skills
- **Syncfusion MAUI Licensing**: https://www.syncfusion.com/maui-controls/licensing

## Support

For issues or questions:
1. Check `references/TROUBLESHOOTING.md` for common MAUI-specific problems
2. Verify your project meets prerequisites (.NET 8+, MAUI 8.0+, Visual Studio 2022 v17.8+)
3. Ensure Syncfusion license is valid and registered (Community/Trial/Commercial)
4. Review generated code for WCAG 2.1 AA compliance report
5. Consult Syncfusion MAUI controls skills library for control-specific questions

---

**Version**: 2.0 (MAUI Framework, Multi-Platform)  
**Last Updated**: May 5, 2026  
**Target Platforms**: iOS 14+, Android 10+, macOS 11+, Windows 10+  
**Framework**: .NET MAUI 8.0+ (.NET 9.0+)  
**Next Phase**: AI-powered component suggestions, design system automation, observability
