# Stage 3: Layout Analysis & Control Mapping (Combined)

**Purpose:** Analyze user requirements, create optimal control-mapping.json, and map to Syncfusion MAUI controls automatically. **FULLY AUTOMATED — NO user interaction.**

---

## Stage 3: Layout Analysis

### AI Should:

1. **Read page/control type** from Stage 1 intent analysis
2. **Analyze user query** for specific requirements and context
3. **Determine optimal layout variant** (no user choice needed)
4. **Create structured control-mapping.json** with all elements
5. **Output JSON** for Stage 3-4 combined processing

### Decision Framework

Based on user requirements, AI selects the **best** layout (not multiple options):

| Page/Control Type | Decision Criteria | Best Variant |
|---|---|---|
| **Login Page** | Enterprise? 2FA needed? Social login? Cross-platform? | Choose: Minimal/Standard/Advanced |
| **Data Grid** | Read-only or editable? Export needed? Sorting/Filtering? | Choose: Simple/Interactive/Full-featured |
| **Dashboard** | Internal or customer-facing? Mobile/Tablet/Desktop layout? | Choose: Focused/Standard/Enterprise |
| **Navigation** | Bottom Tab or Side Drawer? Multi-level needed? | Choose: Simple/TabBar/Drawer/Progressive |
| **Form** | Single-step or multi-step? Validation level? Platform support? | Choose: Basic/Standard/Advanced |

**Key Principle:** AI makes the optimal choice based on the user query context and MAUI best practices. No variant selection UI needed.

### Output: Structured JSON (for Stage 3-4 Control Mapping)

```json
{
  "page_type": "Login Page",
  "variant": "Standard",
  "platform_targets": ["iOS", "Android", "Windows", "macOS"],
  "elements": [
    {
      "id": "email_entry",
      "name": "Email Address",
      "description": "Email field with validation",
      "type_hint": "text entry input email form validation maui"
    },
    {
      "id": "password_entry",
      "name": "Password",
      "description": "Password field, masked input",
      "type_hint": "text entry input password form masked maui"
    },
    {
      "id": "remember_me_checkbox",
      "name": "Remember Me",
      "description": "Keep me logged in",
      "type_hint": "checkbox form input maui control"
    },
    {
      "id": "submit_button",
      "name": "Submit",
      "description": "Login button",
      "type_hint": "button primary action submit cta maui"
    },
    {
      "id": "forgot_password_link",
      "name": "Forgot Password",
      "description": "Password reset link",
      "type_hint": "link text button maui"
    },
    {
      "id": "error_message",
      "name": "Error",
      "description": "Error indicator and message",
      "type_hint": "label text error message validation maui"
    }
  ]
}
```

**JSON Structure Details:**

- `page_type`: The MAUI page/component being built (e.g., "Login Page", "Data Grid Page", "Dashboard")
- `variant`: Chosen variant based on user requirements (e.g., "Standard", "Advanced", "Minimal")
- `platform_targets`: Target platforms (e.g., ["iOS", "Android", "Windows", "macOS"])
- `sections` (optional): For complex layouts, group elements into logical sections
  - `section_id`: Unique identifier (e.g., "header_section")
  - `section_name`: Display name (e.g., "Header")
  - `elements`: Array of elements within section
- `elements`: Array of MAUI control elements
  - `id`: Unique identifier (snake_case)
  - `name`: Display name for UI
  - `description`: What this element does
  - `type_hint`: MAUI control type for BM25 search (e.g., "text entry", "button", "picker", "datagrid", "chart", "maui")

**Important:**
- `type_hint` is critical for Stage 4 ControlMapper BM25 search accuracy (include "maui" keyword)
- Keep descriptions concise and functional
- Use lowercase for `id` and `type_hint`
- Create `control-mapping.json` in project root (reused in Stage 4 & 5)
- Include platform targets for multi-platform MAUI support
- Output minimal summary table in chat for visibility

### Type Hint Best Practices (MAUI)

**For Header/AppBar Elements:**
- Always include `appbar`, `header`, or `shell` keyword in type_hint
- Always include `maui` keyword for MAUI-specific matching
- Examples:
  - Logo: `"image logo appbar header branding maui"` (not just `"image logo"`)
  - Notifications: `"button notification appbar header maui"`
  - User Menu: `"picker button menu user profile appbar maui"`

**General Guidelines:**
- Use **compound keywords** - `"button notification maui"` scores better than `"notification"`
- Include **context keywords** - appbar/header/drawer/tabbar context improves matching
- Include **maui keyword** in all type_hints for MAUI-specific control scoring
- Add **modifiers** - sortable, filterable, collapsible, paginated, selectable
- Reference **control keywords from CSV** - Use exact Syncfusion MAUI control keywords

**Scoring Guide:**
- Score **40+** = Excellent match (multiple keywords + context)
- Score **20-40** = Good match (several keywords)
- Score **<20** = Weak match (may fall back to NATIVE_HTML)
- Score **0** = No match (unrelated keywords)



---

## Complex Layouts (Multi-Section)

For dashboards, admin panels, or multi-section layouts:

```json
{
  "page_type": "Dashboard Page",
  "variant": "Classic Dashboard",
  "platform_targets": ["iOS", "Android", "Windows", "macOS"],
  "layout_pattern": "flex-layout-grid",
  "sections": [
    {
      "section_id": "header",
      "section_name": "Header",
      "description": "Fixed top navigation bar",
      "responsive": "fixed",
      "elements": [
        {
          "id": "logo",
          "name": "Company Logo",
          "description": "Brand logo in header",
          "type_hint": "image logo header branding maui"
        },
        {
          "id": "notification_bell",
          "name": "Notifications",
          "description": "Notification button with count in header",
          "type_hint": "button notification header maui"
        },
        {
          "id": "user_menu",
          "name": "User Profile",
          "description": "User avatar picker menu",
          "type_hint": "picker button menu user profile header maui"
        }
      ]
    },
    {
      "section_id": "drawer",
      "section_name": "Navigation Drawer",
      "description": "Side navigation drawer",
      "responsive": "collapsible",
      "elements": [
        {
          "id": "nav_menu",
          "name": "Navigation Menu",
          "description": "Main navigation",
          "type_hint": "drawer navigation collapsible maui"
        }
      ]
    },
    {
      "section_id": "main_content",
      "section_name": "Main Content",
      "responsive": "flexible",
      "elements": [
        {
          "id": "kpi_cards",
          "name": "KPI Cards",
          "description": "Metric cards displaying statistics",
          "type_hint": "card gridlayout statistics dashboard metrics kpi maui"
        },
        {
          "id": "data_grid",
          "name": "Users Table",
          "description": "Data table with sorting and filtering",
          "type_hint": "datagrid grid table sortable filterable paging maui"
        }
      ]
    }
  ]
}
```

---

## Stage 3-4 File-Based Workflow (TOKEN OPTIMIZATION)

### ⚠️ MANDATORY: Create control-mapping.json in Project Root

**Step 1: Create `control-mapping.json`** in project root (NOT in scripts folder)
```bash
# File: ./control-mapping.json (at MAUI project root)
```

**Step 2: Run ControlMapper Script** with control-mapping.json input
```bash
cd scripts
node controls_search.cjs ../control-mapping.json
```

**Step 3: Capture Output in Chat Context**
- ✅ Script outputs control mapping JSON for Syncfusion MAUI controls
- ✅ Keep mapping results in conversation context ONLY (no file)
- ✅ Do NOT save script output to file
- ✅ Reference mapping in chat for Stages 4-5

### Workflow Benefits
| Aspect | Benefit |
|--------|---------|
| **Token Efficiency** | Create control-mapping.json once, reuse in script without re-passing JSON in chat |
| **Persistence** | `control-mapping.json` stays in project for version control + auditing |
| **Scriptability** | Script reads `control-mapping.json` directly from filesystem (faster) |
| **Clean Context** | Only control mapping output in chat (not redundant control-mapping.json) |
| **Reusability** | Stage 5 code generation can reference `control-mapping.json` if needed |

---

## Stage 3-4 Part 2: Control Picking (Script-Based)

### Input: Control-Mapping JSON from Stage 3

Receive the control-mapping.json created above with control elements mapped to Syncfusion MAUI controls.

### Processing: ControlMapper Script

**⚠️ MANDATORY STEPS (DO NOT SKIP):**

1. **Create** `control-mapping.json` at MAUI project root (e.g., `D:\my-maui-app\control-mapping.json`)
2. **Execute** script with ABSOLUTE PATH (Windows best practice)
3. **Capture** script output in chat context (DO NOT save to file)
4. **Reference** control mapping in subsequent stages

**Execution (REQUIRED) - Windows:**

Use absolute path for reliable execution:
```bash
cd <your-project-root>\<skills-dir>\syncfusion-maui-ui-builder\scripts
node controls_search.cjs <your-project-root>\control-mapping.json
```

**Replace placeholders:**
- `<your-project-root>` = Your MAUI project's root directory (e.g., `d:\my-maui-app`)
- `<skills-dir>` = Skills directory name (configuration-specific, e.g., `.codestudio\skills`, `.agents\skills`, `.github\skills`, or `skills`)

**Real example (with hidden config directory like .codestudio):**
```bash
cd d:\my-maui-app\.codestudio\skills\syncfusion-maui-ui-builder\scripts
node controls_search.cjs d:\my-maui-app\control-mapping.json
```

**Real example (with hidden config directory like .agents):**
```bash
cd d:\my-maui-app\.agents\skills\syncfusion-maui-ui-builder\scripts
node controls_search.cjs d:\my-maui-app\control-mapping.json
```

**Real example (with visible skills directory):**
```bash
cd d:\my-maui-app\skills\syncfusion-maui-ui-builder\scripts
node controls_search.cjs d:\my-maui-app\control-mapping.json
```

**Path Resolution Rules (for Script):**
- ✅ **Absolute paths work best** - Full path from C:\ or D:\ or any drive (most reliable)
- ✅ **Fully IDE-agnostic** - Works with ANY skills directory structure (`.codestudio/`, `.agents/`, `.github/`, `skills/`, or custom)
- ✅ **Framework-independent** - Not tied to React/Vue/Angular, works for MAUI
- ✅ Script automatically resolves relative paths from script location
- ✅ Script validates path exists before processing and shows full path if error occurs
- ❌ Avoid relative paths like `../control-mapping.json` (can cause "file not found" errors)

**Output Destination:**
- ✅ Control mapping JSON printed to terminal (Syncfusion MAUI controls)
- ✅ **Captured in chat context ONLY** (not saved to file)
- ✅ Used for Stage 4 theming + Stage 5 code generation

This automatically:
- Maps elements → Syncfusion MAUI controls (BM25 search)
- Produces Stage 4 output JSON with control mappings
- **Results stay in conversation, not persisted to disk**

### How It Works

1. **Control Mapping**: BM25 search on `type_hint` → finds best Syncfusion MAUI control
2. **Fallbacks**: 
   - No control match → `StackLayout` (native MAUI layout)
3. **Output**: Complete mapping with MAUI controls for Stage 5

### BM25 Search Algorithm

The ControlMapper uses **BM25 (Best Matching 25)**:

- **Tokenizes** query and control keywords (including "maui" keyword for priority)
- **Calculates** term frequency (TF) in each Syncfusion MAUI control
- **Calculates** inverse document frequency (IDF) across all MAUI controls
- **Applies** BM25 formula for semantic relevance ranking
- **Returns** ranked results with scores

**Quality:** Only controls with score > 0 are matched; unmatched → Native MAUI Layout (StackLayout, FlexLayout, Grid)

### Output: Control Mapping

```json
{
  "page_type": "Login Page",
  "variant": "Standard",
  "platform_targets": ["iOS", "Android", "Windows", "macOS"],
  "mapped_controls": [
    {
      "element_id": "email_entry",
      "element_name": "Email Address",
      "control": "SfTextInputLayout",
      "package": "Syncfusion.Maui.Inputs",
      "score": 13.24
    },
    {
      "element_id": "password_entry",
      "element_name": "Password",
      "control": "SfTextInputLayout",
      "package": "Syncfusion.Maui.Inputs",
      "score": 12.87
    },
    {
      "element_id": "remember_me_checkbox",
      "element_name": "Remember Me",
      "control": "SfCheckBox",
      "package": "Syncfusion.Maui.Buttons",
      "score": 11.45
    },
    {
      "element_id": "submit_button",
      "element_name": "Submit",
      "control": "SfButton",
      "package": "Syncfusion.Maui.Buttons",
      "score": 10.89
    },
    {
      "element_id": "forgot_password_link",
      "element_name": "Forgot Password",
      "control": "Label",
      "package": "Microsoft.Maui.Controls",
      "score": 8.5
    },
    {
      "element_id": "error_message",
      "element_name": "Error",
      "control": "Label",
      "package": "Microsoft.Maui.Controls",
      "score": 9.2
    }
  ]
}
```

---

## Status

✅ **FULLY AUTOMATED** - No user interaction
✅ **Single pass** - control-mapping.json created once, controls mapped immediately
✅ **Token efficient** - No duplication or variant selection overhead
✅ **Data-driven** - BM25 semantic search on Syncfusion MAUI controls

✅ **Multi-Platform** - Platform targets (iOS, Android, Windows, macOS) included in mapping
✅ **Ready for Stage 5** - Control mapping feeds directly to XAML code generation

---

## Architecture

- **Input**: User requirements + page/control type from Stage 1
- **Processing**: 
  - Layout analysis → JSON structure with `type_hint` + platform targets
  - ControlMapper (BM25 algorithm on Syncfusion MAUI controls)
- **Output**: 
  - `control-mapping.json` (MAUI project root) - layout structure for Stage 4 & 5
  - Chat summary table - element count, controls matched, platform support
  - "Syncfusion MAUI Controls Selected: [name1], [name2], [name3]"
  - "Platforms: iOS, Android, Windows, macOS"
- **Data sources**: 
  - `scripts/controls.csv` (Syncfusion MAUI controls)
  - `scripts/controls_search.cjs` (BM25 mappers)
- **Artifacts**: `control-mapping.json` (persistent artifact, reused by Stage 5)
- **Context**: Control mapping results kept in conversation (no file artifact)

---

## Stage 3-4 Control Mapping Summary

| Step | Task | Input | Output | Artifact |
|------|------|-------|--------|----------|
| **Stage 3** | Analyze requirements, create control-mapping.json | User requirements + page/control type | `control-mapping.json` with element definitions + platforms | ✅ `control-mapping.json` |
| **Stage 4** | Map elements to Syncfusion MAUI controls (script-based) | `control-mapping.json` | Control mapping results + BM25 scores | Context only (no file) |
| **Stage 5** | Generate XAML code with controls | `control-mapping.json` + control mapping from context | MAUI `.xaml` + `.xaml.cs` + styling | ✅ Page files |
