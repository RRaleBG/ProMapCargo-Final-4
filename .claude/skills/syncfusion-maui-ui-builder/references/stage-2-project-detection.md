# Stage 2: Project Detection

**Purpose:** Auto-detect MAUI project structure, framework configuration, and dependencies to ensure generated code integrates seamlessly.

**AI Should Auto-Detect:**

1. **Framework Type**
   - Scan for: `*.csproj`, `MauiProgram.cs`, `.NET version`
   - Detect: MAUI project structure, target frameworks (net8.0-ios, net8.0-android, net8.0-windows, net8.0-macos)
   - Check: Multi-targeted vs single-platform project
   
2. **Language**
   - MAUI Projects: Always C# (`.cs` files)
   - Check for: `langVersion` in `.csproj` (default: latest C# version)
   - Verify: .NET version in `*.csproj` (required: .NET 8.0 or higher)
   
3. **Styling Strategy**
   - Check for: `*.xaml.css` files → CSS styling enabled
   - Check for: `ResourceDictionary` files (`.xaml`) → Theme/style resources
   - Check for: Syncfusion theme registration in `MauiProgram.cs`
   - Detect: AppTheme configuration (Light/Dark mode support)
   - Default: XAML-based styling with ResourceDictionary
   
4. **View/Control Directory Structure**
   - Common paths: `Views/`, `Controls/`, `Pages/`, `ViewModels/`
   - Detect: MVVM Toolkit or custom ViewModel pattern
   - Find existing control and view patterns
   
5. **Code Style & Formatting Rules**
   - Read `.editorconfig` for indent (spaces vs tabs), line endings
   - Read `.stylecop.json` (if present) for C# naming conventions
   - Check csproj for `TreatWarningsAsErrors`, `Nullable` settings
   - Apply same C# formatting and naming conventions to generated code
   
6. **Syncfusion License & NuGet Package Versioning**
   - Check: Is `SYNCFUSION_LICENSE_KEY` in `appsettings.json` or environment variables?
   - Check: Does `MauiProgram.cs` already call `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("SYNCFUSION_LICENSE_KEY")`?
   - Prompt: If missing, ask user for license key
   - Verify: Syncfusion MAUI services registered in `MauiProgram.cs`
   
7. **Syncfusion NuGet Package Version Detection**
   - **Scan `*.csproj` for existing Syncfusion packages:**
     - If `Syncfusion.Maui.*` packages exist: Extract version (e.g., `26.1.35`)
     - Use SAME version for all new Syncfusion packages → Prevents version conflicts
   - **If NO existing Syncfusion packages found:**
     - Use latest stable version for all new packages: `Syncfusion.Maui.DataGrid` (latest)
   - **Check .NET version compatibility:** Ensure Syncfusion MAUI version supports target .NET (e.g., .NET 8.0)
   - **Document version decision:** Log detected version in stage output
   
**User Interaction:**
Ask user to confirm or override detected settings:
```
✓ Framework: MAUI (>= .NET 9.0)
✓ Language: C# (latest version)
✓ Target Frameworks: net9.0-ios, net9.0-android, net9.0-windows, net9.0-macos
✓ Styling: XAML + ResourceDictionary (AppTheme: Light/Dark)
✓ View Dir: Views/
✓ ViewModel Pattern: MVVM Toolkit
✓ Code Style: 4 spaces, nullable enabled
✓ Syncfusion Version: 26.1.35 (detected from .csproj)
  OR
✓ Syncfusion Version: Latest (no existing packages detected)

[Confirm] [Override] [Cancel]
```

**Status:** User decides whether to accept detected settings or override them.
- If confirmed: Stage 7 (Dependencies) will use detected version for ALL new Syncfusion.Maui.* NuGet packages
- If overridden: User can specify custom version or "Latest" for newest compatible version
