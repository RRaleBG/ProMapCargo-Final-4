# Stage 1: Intent Analysis

**Purpose:** Parse and validate the user's natural language request, identify control type and modifiers, resolve ambiguities for MAUI pages/controls.

**AI Should:**
- Read the user's raw query carefully
- Identify primary intent: `generate_control`, `generate_page`, or `modify_existing`
- Extract control type (e.g., "login form" → page/login with Entry/Button controls, "data list" → page with CollectionView/DataGrid)
- Extract modifiers (e.g., "dark theme" → styling:dark, "with platform support" → feature:cross-platform)
- Identify target directory if specified (e.g., "in the Auth folder" → targetDir:Views/Auth/)

**Ambiguity Resolution:**
If the request is unclear, ask ONE clarifying question. Examples:

| Ambiguous Input | Clarifying Question |
|---|---|
| "Build me a form" | "What kind of form? (login, registration, contact, multi-step) for what platform? (iOS, Android, Windows, macOS, or all?)" |
| "Add a control" | "What control would you like? (ListView, DataGrid, DatePicker, ComboBox, Entry, etc.) and what layout?" |
| "Make it better" | "Which control/page and what aspect? (accessibility, styling, responsive layout, performance)" |

**Output to User:**
One-line confirmation:
```
✓ Understood: Generating a dark-themed MAUI login page with Entry controls, "Remember Me" checkbox, and cross-platform support.
Starting project detection...
```

**Reference:** For MAUI control and page type catalog, see LAYOUT-DESIGN.md and MAUI-CONTROLS.md

**Status:** This stage requires NO user interaction for confirmation. AI decides intent based on pure reasoning and MAUI architectural patterns.
