# Stage 5: Code Generation

**Purpose:** Generate production-ready MAUI code, XAML views, C# models, and ViewModels with accessibility and cross-platform compliance (iOS, Android, Windows, macOS).

## CRITICAL: Read Control Skills BEFORE Code Generation

**THIS STEP IS NOT OPTIONAL - Must be completed before writing any code**

### Step 1: Identify All Control Skills from Stage 3

From Stage 3 output, extract ALL Syncfusion MAUI control skills used (e.g., `syncfusion-maui-datagrid`, `syncfusion-maui-textentry`, `syncfusion-maui-buttons`, `syncfusion-maui-datepicker`, etc.)

### Step 2: Read Getting-Started for EACH Control Skill

**For every single control skill identified in Step 1:**

1. **Read:** `.codestudio/skills/<skill-name>/references/getting-started.md`
   - This is the ONLY authoritative source for usings, XAML markup, and setup
   - Do NOT generate code without reading this first
   
2. **Extract and document:**
   - NuGet package name (e.g., `Syncfusion.Maui.DataGrid`)
   - Exact using statement for the control (e.g., `using Syncfusion.Maui.DataGrid;`)
   - **XAML namespace declarations (CRITICAL)** - e.g., `xmlns:syncfusion="clr-namespace:Syncfusion.Maui.DataGrid;assembly=Syncfusion.Maui.DataGrid"`
   - MauiProgram.cs setup code (if needed)
   - Platform-specific considerations (iOS, Android, Windows, macOS)
   - Base dependencies

### Step 3: If Complex Features Needed

- Read: `.codestudio/skills/<skill-name>/SKILL.md` for complete API documentation
- Read feature-specific guides: `<skill-name>/references/filtering.md`, `validation.md`, `styling.md`, etc.

### Step 4: Read the Syncfusion MAUI themes guide to register the overall Syncfusion theme:
1. **Read:** `.codestudio/skills/syncfusion-maui-ui-builder/references/syncfusion-maui-themes.md`
2. **Verify:** MauiProgram.cs has `UseSyncfusionLicense()` and `.ConfigureSyncfusionCore()` 

### Step 5: NOW Generate Code Using Extracted Information

Only after completing Steps 1-4, generate the .xaml file using the exact usings and XAML namespace declarations extracted from control skills.

**Common Mistake to Avoid:**
❌ Generate code, then try to add XAML namespace declarations later → Results in missing controls, compilation errors
✅ Read getting-started FIRST, extract namespace declarations and usings, THEN generate code with all usings and XAML included

**Why This Order Is Critical:**
- Control skills contain the authoritative XAML and C# syntax
- XAML namespace declarations are often forgotten and cause runtime errors
- Reading first ensures: correct usings, correct namespace declarations, no missing dependencies, error-free code
- Compatibility with Syncfusion MAUI skill updates and cross-platform requirements (iOS, Android, Windows, macOS)

---

## Code Generation Process

**After reading all control skills, AI Should:**

1. **Generate .xaml view file**:
   - MAUI ContentPage or custom layout
   - Proper Syncfusion MAUI control declarations with namespace
   - XAML binding expressions for data and commands
   - Event handlers via behaviors or EventToCommand
   - Validation and error handling (via converters or code-behind)
   - Accessibility markup (AutomationId, SemanticDescription, keyboard navigation)
   - Comments explaining usage and layout

2. **Generate .xaml.cs code-behind file**:
   - MAUI ContentPage or Page class
   - Event handlers if needed (in addition to XAML bindings)
   - Cross-platform considerations (iOS, Android, Windows, macOS)
   - Safe area handling (notches, status bars)
   - Lifecycle management (OnAppearing, OnDisappearing)

3. **Generate ViewModel.cs file** (if using MVVM):
   - INotifyPropertyChanged implementation
   - Command bindings (ICommand)
   - Data models and properties
   - Validation logic

4. **Generate ResourceDictionary or Styling** (based on Stage 4 strategy):
   - XAML resources (Colors, Sizes, Typography)
   - Implicit styles (if using implicit style strategy)
   - XAML.CSS files (if using CSS styling)
   - Dark mode support (AppTheme detection)

5. **Reference code standards** from:
   - MAUI-standards.md (accessibility + security rules)
   - Control skill's feature-specific guides (filtering.md, validation.md, styling.md, etc.)

**Code Generation Standards:**

- **Control Usings:** Use exact using syntax from control skill's getting-started.md
- **XAML Namespace:** Include Syncfusion MAUI namespace declarations correctly
- **Semantic XAML:** Use proper MAUI controls (Entry, Button, Label, etc.), not generic containers
- **Accessibility:** AutomationId, SemanticDescription, keyboard navigation, 44x44px touch targets
- **C# Code:** No warnings, nullable reference types enabled, proper null checks
- **Error Handling:** Try-catch blocks, user-friendly error messages in UI
- **Responsive:** MAUI layouts (StackLayout, FlexLayout, Grid), OnSizeAllocated for platform-specific behavior
- **Performance:** Data virtualization for lists, async/await for I/O operations
- **Security:** No hardcoded secrets, validate/sanitize user input, use secure storage for sensitive data
- **Cross-Platform:** Handle platform differences (iOS safe areas, Android permissions, Windows window sizing, macOS app lifecycle)
- **Comments:** XML documentation (///) on public methods, explain complex logic

### Media (MANDATORY)

- **Placeholder Images:** Use [Unsplash](https://unsplash.com) for high-quality placeholder images
  - Format: `https://images.unsplash.com/photo-[id]?w=[width]&h=[height]&fit=crop`
  - Example: `https://images.unsplash.com/photo-1560472354-b33ff0c44a43?w=200&h=100&fit=crop`
  - Always specify dimensions (width x height) in the URL
  - Use relevant keywords for context-appropriate images

### Button & Icon Styling with MAUI (MANDATORY)

**Principle:** Let MAUI Syncfusion controls own button dimensions and styling. Use only necessary properties for layout and state.

❌ **INCORRECT** - Overriding Syncfusion button with too many custom styles:
```xaml
<SfButton Text="Play"
          Padding="16"
          CornerRadius="10"
          FontSize="18"
          BorderWidth="2"
          BorderColor="Blue"
          BackgroundColor="LightBlue" />
```

✅ **CORRECT** - Let Syncfusion control handle styling, apply only essential properties:
```xaml
<StackLayout Spacing="8">
  <SfButton Text="Play"
            Command="{Binding PlayCommand}"
            CornerRadius="4" />
  
  <SfButton Text="More Info"
            Command="{Binding InfoCommand}"
            CornerRadius="4" />
</StackLayout>
```

**Why This Works:**
- Syncfusion MAUI controls have built-in Material 3 design
- Button sizing aligns with platform conventions
- Consistent appearance across all Syncfusion controls
- Accessibility properties handled automatically
- Touch targets respect 44×44 dp minimum

---

### Component Reuse Across UI (Same Component, Multiple Places)

**Principle:** One Syncfusion component type can be reused throughout your UI with customizations. For example, `ButtonComponent` can serve as the Login button, Forgot Password link, and Sign Up button—each customized via CSS variables.


**Example - Button Used in Multiple Places (MAUI XAML):**
```xml
<!-- LoginPage.xaml -->
<VerticalStackLayout Spacing="16">
      <!-- Primary Login Button -->
      <syncfusion:SfButton Text="Login"
                                    Command="{Binding LoginCommand}"
                                    CornerRadius="4"
                                    AutomationId="LoginButton" />

      <!-- Flat Button as Link -->
      <syncfusion:SfButton Text="Forgot Password?"
                                    Command="{Binding ForgotPasswordCommand}"
                                    CornerRadius="4"
                                    Style="{StaticResource FlatButtonStyle}"
                                    AutomationId="ForgotPasswordButton" />

      <!-- Outline Button as Secondary Action -->
      <syncfusion:SfButton Text="Sign Up Here"
                                    Command="{Binding SignUpCommand}"
                                    CornerRadius="4"
                                    Style="{StaticResource OutlineButtonStyle}"
                                    AutomationId="SignUpButton" />
</VerticalStackLayout>
```

```xml
<!-- App.xaml (ResourceDictionary for button styles) -->
<ResourceDictionary>
      <!-- Flat Button Style -->
      <Style x:Key="FlatButtonStyle" TargetType="syncfusion:SfButton">
            <Setter Property="BackgroundColor" Value="Transparent" />
            <Setter Property="TextColor" Value="#6c757d" />
            <Setter Property="BorderWidth" Value="0" />
            <Setter Property="FontSize" Value="14" />
            <Setter Property="TextDecorations" Value="Underline" />
      </Style>
      <!-- Outline Button Style -->
      <Style x:Key="OutlineButtonStyle" TargetType="syncfusion:SfButton">
            <Setter Property="BackgroundColor" Value="Transparent" />
            <Setter Property="TextColor" Value="#6c757d" />
            <Setter Property="BorderColor" Value="#6c757d" />
            <Setter Property="BorderWidth" Value="1" />
      </Style>
</ResourceDictionary>
```

---

### Reading Component Skills BEFORE Using Components (MANDATORY)

**CRITICAL:** Do NOT assume component properties or APIs.

**Required Process:**
1. **Identify all mapped components** from Stage 3 output
   - E.g., GridComponent, ChartComponent, SidebarComponent, etc.

2. **For EACH component**, read the component skill:
   - Location: `.codestudio/skills/<component-skill>/references/getting-started.md`
   - Extract: imports, style imports, required props, setup code
   - Read: feature-specific guides (filtering, sorting, validation, styling, etc.)

3. **DO NOT generate code without reading** component skill documentation
   - Don't assume prop names or API structure
   - Don't guess at event handler names
   - Don't skip required setup or initialization

**Example - Reading DataGrid Control Skill:**
```
Before generating code:
1. Read: .codestudio/skills/syncfusion-maui-datagrid/references/getting-started.md
   → Extract: NuGet package: Syncfusion.Maui.DataGrid
   → Extract: using statement: using Syncfusion.Maui.DataGrid;
   → Extract: XAML namespace: xmlns:syncfusion="clr-namespace:Syncfusion.Maui.DataGrid;assembly=Syncfusion.Maui.DataGrid"
   → Read: MauiProgram.cs setup, columns configuration, data binding

2. Read: .codestudio/skills/syncfusion-maui-datagrid/references/sorting.md
   → Understand: SortingMode, SortColumn, ascending/descending

3. Read: .codestudio/skills/syncfusion-maui-datagrid/references/filtering.md
   → Understand: AllowFiltering, FilterRow, filter operators

4. NOW generate code with correct namespace, XAML markup, and C# code
```

**What Component Skills Contain:**
- ✅ Authoritative import statements
- ✅ Required style imports (CRITICAL - don't skip)
- ✅ Complete API documentation
- ✅ Feature-specific patterns (sorting, filtering, validation)
- ✅ Best practices and performance considerations
- ✅ Accessibility requirements
- ✅ Theme customization options

**Common Mistakes to Avoid:**
- ❌ Guessing prop names → Read skill documentation
- ❌ Missing style imports → Extract from getting-started.md
- ❌ Wrong event handler names → Copy from component skill examples
- ❌ Incomplete setup code → Follow skill's recommended initialization

---

**Example Output Files:**

```
Views/
   ├── LoginPage.xaml              (MAUI ContentPage XAML)
   ├── LoginPage.xaml.cs           (Code-behind)
   └── LoginViewModel.cs           (ViewModel for MVVM)
Resources/
   └── Styles.xaml                 (ResourceDictionary for styles)
```

---

## Component Integration & File Mapping

**Generated files MUST be wired to display in the app:**


1. **Register View in AppShell.xaml**:
   ```xml
   <!-- AppShell.xaml -->
   <ShellContent Title="Login" ContentTemplate="{DataTemplate local:LoginPage}" />
   ```

2. **Set MainPage in App.xaml.cs**:
   ```csharp
   public App()
   {
       InitializeComponent();
       MainPage = new AppShell();
   }
   ```

3. **Ensure ResourceDictionary and styles are loaded**:
   - Add styles to `App.xaml` or `Resources/Styles.xaml`
   - Syncfusion MAUI theme imported in `MauiProgram.cs` (Stage 4)
   - AppTheme (light/dark mode) configured in `MauiProgram.cs`

**Without this mapping, the page or component will not render in the app.**

**User Interaction:** 
Optional review of generated code. No blocking confirmation.

**Status:** AI generates without user decision. User can review/adjust if needed.
