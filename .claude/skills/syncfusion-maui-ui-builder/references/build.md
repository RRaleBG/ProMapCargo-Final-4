# Troubleshooting & FAQ (AI Agent Reference)

**Version:** 1.0.0  
**Last Updated:** April 15, 2026  
**Purpose:** Agent reference for handling MAUI build errors, control issues, and user troubleshooting

---

## ⚠️ AGENT PROTOCOL: Build/Control Issues

**When user reports build errors or control issues:**
1. **Identify the control type** (e.g., SfTextInputLayout, SfDataGrid, SfDatePicker, etc.)
2. **Load the corresponding control skill** immediately (TextInputLayout Skill, DataGrid Skill, etc.)
3. **Reference the control skill's troubleshooting section** for setup, usings, and solutions
4. **Direct user to control skill** if issue persists
5. Do NOT generate solutions without consulting control skill documentation first

---

## Table of Contents

1. [Installation & Setup](#installation--setup)
2. [Build & Runtime Errors](#build--runtime-errors)
3. [Component Issues](#component-issues)

---

## Installation & Setup Issues

When user encounters setup errors:

### Syncfusion NuGet package not found
→ **Direct to control skill** for exact package list. Each control skill lists required `Syncfusion.Maui.*` NuGet packages.

### .csproj not found
→ **Verify user is in MAUI project root.** Guide to project detection (Stage 2).

### .NET version incompatible (< 8.0)
→ **Inform user** to upgrade: `dotnet new maui -n ProjectName` with latest .NET 8.0 or higher

---

## Build & Runtime Error Protocol

### XAML parse errors
→ Verify generated XAML syntax, namespace declarations, and control naming. Check XAML compiler output.

### Missing control usings (e.g., "Cannot find SfDataGrid")
→ **Load control skill** (TextEntry, DataGrid, etc.). Reference required usings and NuGet package names.

### Syncfusion license errors
→ Ensure Syncfusion NuGet packages installed. Check if license registration code is in MauiProgram.cs.

### Event handler issues
→ **Load control skill.** Each control has specific event structure (EventToCommand binding, command parameters, etc.).

---

## Control-Specific Issues Protocol

**Any control-specific issue → Load corresponding control skill IMMEDIATELY**

Examples of control-specific issues:
- SfTextInputLayout not showing value → TextInputLayout Skill troubleshooting
- SfDataGrid pagination not working → DataGrid Skill troubleshooting
- SfDatePicker format wrong → DatePicker Skill troubleshooting
- SfComboBox filtering not enabled → ComboBox Skill troubleshooting

**Control skill structure always includes:**
- Required properties and default values
- Event/command handler patterns (ICommand bindings, EventToCommand usage)
- Common pitfalls and solutions
- Syncfusion service registrations (if needed in MauiProgram.cs)

---

## Agent Guidelines

**When user reports issues:**
1. Classify error type (setup / build / control-specific / feature request)
2. Setup/build issues → Use this troubleshooting guide
3. Control issues → **Load control skill** immediately
4. Feature gaps → Suggest control skill or custom implementation
5. Ambiguous errors → Ask clarifying questions before loading skills

**When checking control skills:**
- Navigate to control skill (TextInputLayout, DataGrid, Button, DatePicker, etc.)
- Review "Getting Started" for setup and usings
- Check "Common Issues" section for troubleshooting
- Reference "Properties" and "Events/Commands" for implementation details

**If issue NOT resolved by control skill:**
→ Escalate to user with control skill reference
→ Suggest manual fix with C#/XAML code example
→ Do NOT generate new code without understanding root cause

