# Stage 6: Validation

**Purpose:** Validate generated MAUI code against .NET and accessibility standards. Binary pass/fail result.

**AI Should:**

1. **Validate MAUI Accessibility Compliance (WCAG 2.1 AA equivalent):**
   - AutomationId assigned to interactive controls?
   - SemanticDescription provided for complex controls?
   - Keyboard navigation supported? (Tab order, focus management)
   - Color contrast ≥ 4.5:1 for text on background?
   - Focus indicator visible on all interactive elements?
   - Screen reader support via AutomationProperties?

2. **Check Security:**
   - No hardcoded secrets/API keys in code-behind or XAML
   - Input validated before data binding
   - No unsafe string interpolation in XAML bindings
   - No reflection used without proper validation
   - Secure storage for sensitive data (SecureStorage API in MAUI)

3. **Verify Performance:**
   - Data binding implemented efficiently (avoid binding to methods)?
   - CollectionView/DataGrid uses virtualisation for large lists?
   - No memory leaks in event subscriptions (proper unsubscribe)?
   - Images optimized and cached appropriately?
   - No blocking operations on main thread?
   - Async/await used for long-running operations?

4. **Check Cross-Platform Compatibility:**
   - Platform-specific layouts handled via x:OnPlatform?
   - Safe areas handled for notches (iOS) and system UI (Android)?
   - Responsive layout using Grid/FlexLayout for all screen sizes?
   - Touch targets ≥ 44x44 device-independent units?
   - Visual hierarchy consistent across iOS, Android, Windows, macOS?

**Validation Result:**

Binary: **PASS ✓** or **FAIL ✗**

**If PASS:**
```
✓ Validation Result: PASS

All standards met:
  ✓ MAUI accessibility (AutomationId, SemanticDescription)
  ✓ Security checks
  ✓ Performance standards (virtualization, async/await)
  ✓ Cross-platform compatibility (iOS, Android, Windows, macOS)
  ✓ Code quality and C# standards

Ready to proceed to Stage 7 (Dependencies)...
```

**If FAIL:**
```
✗ Validation Result: FAIL

Issues found:
  ✗ Color contrast on Label (3.2:1, need 4.5:1)
  ✗ SfTextEntry missing AutomationId attribute
  ✗ CollectionView not using virtualization for 500+ items

Auto-fixes applied:
  ✓ Increased FontSize for contrast
  ✓ Added AutomationId to SfTextEntry
  ✓ Added ItemsUpdatingScrollMode="KeepItemsInView" to CollectionView

Remaining issues: 0
Result: PASS (after fixes)
```

5. **Check Code Quality:**
   - Proper C# naming conventions (PascalCase for classes, camelCase for properties)?
   - XML documentation comments (///) on public members?
   - No null reference exceptions (proper null checks or nullable types)?
   - IDisposable pattern used for resource cleanup (timers, event subscriptions)?
   - No circular dependencies in ViewModels?

**User Interaction:** ⭐ **USER DECISION #6**

If result is PASS:
```
Ready to generate dependencies?
[Proceed] [Review] [Stop]
```

If result is FAIL (after fixing):
```
Validation failed with 2 issues (not auto-fixable):
  - CollectionView needs custom renderer for platform-specific pagination
  - ViewModel requires custom INotifyPropertyChanged for complex property chains

Override and proceed anyway?
[Override & Proceed] [Request Manual Fixes] [Stop]
```

**Status:** ⭐ **USER DECISION #6** - User confirms validation result or overrides.

**Reference:** See `validation-rules.md` in `syncfusion-maui-ui-builder/references/` for complete MAUI validation rules and correction methods.

**Examples of Common Validation Issues & Fixes:**

| Issue | Example | Fix |
|-------|---------|-----|
| Missing AutomationId | `<SfTextEntry Placeholder="Name" />` | `<SfTextEntry Placeholder="Name" AutomationId="nameInput" />` |
| Color contrast failure | `<Label Text="Info" TextColor="#888888" />` | `<Label Text="Info" TextColor="#333333" />` (darker) |
| No virtualization | `<CollectionView ItemsSource="{Binding Items}" />` | `<CollectionView ItemsSource="{Binding Items}" ItemsUpdatingScrollMode="KeepItemsInView" />` |
| Unsafe binding | `<Label Text="{Binding GetUserData()}" />` | Use ViewModel method called on load, bind to property |
| Platform inconsistency | `<Label FontSize="12" />` (Android only) | `<Label FontSize="12" x:OnPlatform="iOS=14, Android=12" />` |
| Memory leak | Event subscription without unsubscribe | Implement IDisposable and unsubscribe in Dispose() |
