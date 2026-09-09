# Theme UI maintenance

- Native Fluent and custom material surfaces share issue data, save handling, and available-width table layout (IssueTableLayout).
- Custom menus use WinUI MenuFlyout. Do not restore the old Canvas overlay, manual popup coordinates, or visual-tree menu traversal.
- Theme changes snapshot editor and quick-add values before updating resources. Switching themes must not save the snapshot to disk.
- Keep primary editor commands outside the scrollable fields. Custom inspector stacks when content width is below 900 DIPs; filters wrap below 640 DIPs.
- Backdrop sources must not be fed through transform/displacement graphs or nested composition effect brushes. The previous implementation generated "Backdrop brushes cannot be transformed" exceptions. Use supported blur/saturation/color graphs, with decorative movement on visuals.
- The two full page trees are still separate. Further extraction should preserve native Fluent control templates and custom material layers; current shared boundaries are tables, menus, data, and save behavior.

## Verification

With the app running, run these PowerShell scripts with its PID:

    ./theme-ui-tests.ps1 -AppPid <pid> -Mode light
    ./menu-ui-tests.ps1 -AppPid <pid>
    ./ui-tests.ps1 -AppPid <pid>

For dark appearance, launch with:

    winapp run ./TodoApp/TodoApp.csproj --arch x64 --detach --json --args '--theme dark'

Then run the theme script with -Mode dark. Mode labels screenshots; it does not change app appearance.

Theme tests cover three custom themes at 1440, 1000, and 560 pixels, pinned command bounds, unsaved title retention, board/list switching, and return to Fluent. Menu tests create and delete their own temporary issue and verify persisted dropdown values. Output is written under test-artifacts.

High Contrast and full keyboard-only navigation are not covered by these scripts.

Reference: [Microsoft Composition brush combinations](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes).

## Keyboard-focus crash regression (2026-09-09)

The later E_UNEXPECTED crash was separate from the earlier backdrop-transform error. Resolving the 17:47 dump's WinUI addresses identified CFocusRectManager::DetermineRenderOptions and UpdateFocusRect. It reproduced when sending Tab to a custom button in Liquid Glass.

Three custom styles enabled system focus visuals while assigning the gradient LiquidGlassEdgeGlowBrush to FocusVisualPrimaryBrush. WinUI's focus renderer explicitly fail-fasts on non-SolidColorBrush inputs. These styles now use the solid LiquidGlassAccentBrush; keyboard focus remains enabled.

Run the keyboard-aware regression after launching the app:

    ./focus-resize-ui-tests.ps1 -AppPid <pid>

This exercises 60 focus cases across three custom themes and four widths, resizing while keyboard focus is active. The earlier InvokePattern-only theme tests did not exercise this path.

Source: [WinUI FocusRectManager brush requirements](https://github.com/microsoft/microsoft-ui-xaml/blob/main/dxaml/xcp/components/FocusRect/FocusRectManager.cpp#L177).

Verification after the focus-brush fix: x64 build passed with zero warnings/errors; light and dark each passed 60 keyboard-focus/resize cases; core UI suite passed 13 checks; editor-menu persistence, filters, and temporary-issue deletion passed. Generated verification output is ignored by Git.
