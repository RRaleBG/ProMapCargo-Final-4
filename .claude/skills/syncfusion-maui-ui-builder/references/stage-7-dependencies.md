# Stage 7: Dependencies

**Purpose:** Detect required NuGet packages, resolve version conflicts, prepare dotnet add package command.

**AI Should:**

1. **Detect Required NuGet Packages:**
   - Scan generated XAML and C# code for using statements and namespaces
   - List all Syncfusion.Maui.* packages used
   - Check for other dependencies (.NET packages, third-party libraries, etc.)

2. **Check Project's .csproj File:**
   - What NuGet packages already installed?
   - What versions are currently in use?
   - Any version conflicts or framework mismatches?

3. **Resolve Conflicts:**
   - If Syncfusion.Maui package already installed:
     - Is version compatible with target .NET version?
     - Suggest upgrade if needed
   - If target framework conflicts with package requirements:
     - Recommend resolution (update .NET version, find alternative package, or use compatibility shim)

4. **Prepare Installation Command:**
   - Generate dotnet add package commands
   - List packages to add
   - List packages to upgrade (if needed)
   - Verify MauiProgram.cs registration requirements

**Example Output:**

```
✓ Dependency Analysis

New Packages to Add:
  - Syncfusion.Maui.DataGrid (latest, requires UseSyncfusionLicense in MauiProgram.cs)
  - Syncfusion.Maui.Buttons (latest)

Existing Packages:
  ✓ Microsoft.Maui.Controls@8.0.0 (compatible)
  ✓ CommunityToolkit.Mvvm@8.2.0 (compatible)

Conflicts: None

Installation Commands (Windows):
$ dotnet add package Syncfusion.Maui.DataGrid
$ dotnet add package Syncfusion.Maui.Buttons

Or as a single line:
$ dotnet add package Syncfusion.Maui.DataGrid Syncfusion.Maui.Buttons

MauiProgram.cs Registration Required:
✓ Add using Syncfusion.Maui.Controls;
✓ Add .UseSyncfusionLicense("YOUR_LICENSE_KEY") to MauiProgram builder
✓ Add .ConfigureSyncfusionCore() to builder
```

**5. **Verify MauiProgram.cs Integration:**
   - Check if license key configuration exists
   - Verify ConfigureSyncfusionCore() is called
   - Ensure all control namespaces are properly declared

**User Interaction:**
User confirms dotnet restore or does it manually:
```
Ready to restore dependencies?
[Restore] [Show Commands] [Skip]
```

**Status:** AI detects and prepares. User decides whether to restore now or later via `dotnet restore` or Visual Studio package manager.
