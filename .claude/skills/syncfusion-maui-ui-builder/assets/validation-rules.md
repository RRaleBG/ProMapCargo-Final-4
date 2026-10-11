# Validation Rules Reference

**Purpose:** Comprehensive checklist for Stage 6 validation. Used to validate MAUI component against MAUI standards.

## Binary Validation Result

Each component receives a **PASS ✓** or **FAIL ✗** result against these rules.

---

## Accessibility (WCAG 2.1 AA equivalent for MAUI) - Blocking

| Rule | Check | Pass/Fail |
|------|-------|-----------|
| **Semantic XAML** | All interactive elements use proper MAUI controls (Entry, Button, Picker, etc.) | |
| **AutomationId** | Form inputs have `AutomationId` for screen reader support | |
| **SemanticDescription** | Invalid inputs have `SemanticDescription` + error message | |
| **Focus Indicator** | All interactive elements have visible focus state | |
| **Keyboard Nav** | Tab order follows visual flow, no keyboard traps | |
| **Color Contrast** | Text ≥ 4.5:1, focus indicator ≥ 3:1 contrast | |
| **Touch Targets** | Interactive elements ≥ 44x44px (mobile-friendly) | |

---

## Security - Blocking

| Rule | Check | Pass/Fail |
|------|-------|-----------|
| **No Unsafe HTML** | No `HtmlWebViewSource` with unsanitized content in WebView controls | |
| **Input Validated** | User input validated/sanitized before binding to model | |
| **No Secrets** | No hardcoded API keys, JWT, or database URLs | |
| **Syncfusion License** | License key in app configuration or environment, not hardcoded | |

---

## Performance - Warning (Auto-fixable)

| Rule | Check | Status |
|------|-------|--------|
| **Control Reuse** | Heavy MAUI controls use data virtualization (DataGrid, CollectionView) | ⚠️ Can auto-fix |
| **Binding Efficiency** | Bindings use OneTime/OneWay when not needed for TwoWay | ⚠️ Can auto-fix |
| **Assembly Size** | No unnecessary assemblies or NuGet packages referenced | ⚠️ Can warn |

---

## Responsive Design - Warning (Auto-fixable)

| Rule | Check | Status |
|------|-------|--------|
| **Mobile-First** | XAML layout optimized for mobile first, then tablet/desktop | ⚠️ Can auto-fix |
| **FlexLayout/Grid** | No fixed widths, uses responsive MAUI layouts (FlexLayout, Grid, StackLayout) | ⚠️ Can auto-fix |
| **Device Triggers** | AppTheme and device size breakpoints handled (phone 320px, tablet 768px, desktop 1024px+) | ⚠️ Can auto-fix |

---

## Code Quality - Warning

| Rule | Check | Status |
|------|-------|--------|
| **C# Types** | Proper type annotations, no implicit `object` types | ⚠️ Can auto-fix |
| **XML Documentation** | Public classes and methods have XML doc comments (`///`) | ⚠️ Can add |
| **Error Handling** | Try-catch on async operations, user-friendly error messages in UI | ⚠️ Can auto-fix |

---

## Validation Logic (Stage 6)

### Step 1: Check Blocking Rules
If ANY blocking rule fails → **FAIL ✗**
- Inaccessible form (unsemantic XAML, missing AutomationId)
- Security vulnerability (unsanitized WebView content, hardcoded secrets)
- Hardcoded secrets or license keys
- Focus trap or keyboard navigation issues

**Action:** Auto-fix if possible. If not auto-fixable, ask user to override or request fixes.

### Step 2: Check Auto-Fixable Warnings
Apply auto-fixes:
- Missing color contrast → Adjust colors
- Missing AutomationId → Add automation identifiers
- Poor keyboard navigation → Fix tab order in XAML
- Missing touch target size → Increase button/control size to ≥44x44px
- Missing responsive styles → Add responsive layout containers (FlexLayout, Grid)

### Step 3: Check Non-Auto-Fixable Warnings
Report to user:
- Missing XML documentation comments (`///`)
- Could benefit from data virtualization on large datasets
- No error handling for async operations

**Action:** Warn but allow proceeding.

### Step 4: Output Result

**If all blocking rules pass + warnings auto-fixed:**
```
✓ VALIDATION PASS

All standards met:
  ✓ WCAG 2.1 AA accessibility (MAUI equivalent)
  ✓ Security checks
  ✓ Performance optimizations
  ✓ Responsive design
  
Auto-fixes applied: 3
  - Fixed color contrast on labels
  - Added AutomationId to inputs
  - Updated responsive layout for tablet/desktop

Ready to proceed to Stage 7...
```

**If blocking rule fails (not auto-fixable):**
```
✗ VALIDATION FAIL

Critical issues:
  ✗ Form controls missing semantic XAML structure
  ✗ No keyboard navigation support

Auto-fixes NOT available for these issues.

Options:
  [Override & Proceed] [Request Manual Fixes] [Cancel]
```

---

## Override Behavior

If user overrides failed validation:
```
⚠️  Proceeding with known accessibility/security issues:
  - Color contrast: 3.8:1 (need 4.5:1)
  - Missing AutomationId on 2 inputs

Code will be generated but flagged as non-compliant.
User assumes responsibility for fixing before production deployment.
```

---

All validation rules ensure generated MAUI code is accessible, secure, and production-ready for iOS, Android, macOS, and Windows platforms.
