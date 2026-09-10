# RUOK validation

Use synthetic values, not real wellbeing records, when testing.

## Animated nebula verification

- Debug and Release app builds passed with zero warnings/errors. The existing 56 focused breathing-session tests passed.
- Native pixel comparisons verified ambient animation in Ready, Paused, Stopped, reloaded, and Completed states. Inhale/exhale changed the visible diameter from approximately 177 to 233 physical pixels in the tested layout. Pause preserved the breathing size while cloud motion continued.
- Reduced motion produced identical captured frames, including while the breathing timeline advanced. The original presentation preference was restored; reminder settings, consent, and wellbeing entries were not changed.
- Offscreen and minimized checks compared the scene before/after a three-second suspension against its normal visible motion. The viewport check temporarily shortens the window so the **entire control**, not just its colored center, is outside the viewport, then restores size and scroll position.
- Repeated page unload/reload restored an animated orb. A full one-minute exercise completed, kept ambient animation, navigated safely after completion, and restarted/stopped successfully.
- Native inspection caught and corrected initial clipping beneath the pinned controls. The orb now uses the original 220-DIP visual slot. Compact primary-action checks continued to pass.
- The three intermediate native app instances exited through normal user-selected Quit with launcher exit code zero. This does not resolve or attribute the earlier intermittent native shutdown failure documented below.
- Full native high-contrast/Windows-animation-setting transitions, extended GPU/power profiling, broad DPI/text-size coverage, and forced texture-load-failure testing remain manual coverage. The implementation includes a distinct high-contrast outline, a live Windows animation-setting subscription, and an explicit visual-load error message.

Run the focused timeline tests:

```powershell
dotnet test .\tests\RUOK.Domain.Tests\RUOK.Domain.Tests.csproj --filter "FullyQualifiedName~BreathingSessionTests"
```

Run actual animation/visibility checks through the existing UI smoke runner:

```powershell
.\tests\Invoke-UiSmoke.ps1 -ProcessId 12345 -CheckNebula -CheckCompletion -CheckCompactLayout
```

Keep RUOK foreground and unobscured, use a one-minute exercise, and enable Windows animations with high contrast off for the moving-frame assertions. The optional [nebula helper](../tests/Nebula-VisualChecks.ps1) uses Windows UI Automation and guarded RUOK-only screen regions. Frames stay in memory; it saves no screenshots. It temporarily toggles and restores the app's reduced-motion preference, resizes/restores the window, scrolls/restores the exercise viewport, and minimizes/restores RUOK. Complete any draft presentation edits before running it.

## Phase 3 verification

- Added 41 notification tests: safe legacy defaults, timing/window/weekday boundaries, overnight windows, monotonic idle/return grace including sleep gaps, raw activation parsing, malformed/stale intents, face-action preference, test/skip/consent/disabled guardrails, XML action counts and labels, duplicate suppression, and protected preference persistence.
- Debug and Release builds passed with zero warnings/errors. All 71 affected infrastructure/application/export tests passed in both configurations, including the 41 new cases. The unchanged 142 domain tests belong to the previously verified baseline below.
- The native Debug and Release apps passed all six screens, five accessible mood radios, breathing controls, and compact-layout smoke checks. Release additionally passed selecting every face with exactly one matching selection and clearing it via Skip without saving.
- Native capture confirmed five full face tiles without separate radio circles. Factor text was corrected to left-align beside the glyph rather than floating in the center of each grid cell.
- The engineer confirmed that the real Windows banner displayed five faces, then moved to Notification Center after a few seconds, and that clicking the Good preview face opened PulseCheck with Good selected and a no-save message.
- In a separately prepared cold-start test, RUOK exited completely with code zero; the engineer then confirmed that the preview face restarted RUOK and opened the correct preselected/no-save PulseCheck.
- [Invoke-TraySmoke.ps1](../tests/Invoke-TraySmoke.ps1) passed: Close hid RUOK without ending its process, normal app activation restored the same window, and the original Off reminder preference was restored. No check-ins were created.
- Native notification banner discovery through UI Automation was unreliable on this desktop; actual banner/face validation above used the engineer's observation, not a claimed automated pass.

Focused notification tests:

```powershell
dotnet test .\tests\RUOK.Infrastructure.Tests\RUOK.Infrastructure.Tests.csproj --filter "FullyQualifiedName~NotificationTests"
```

Tray testing requires saved reminders to be Off and the tray preference selected. Finish any draft reminder edits first. It temporarily enables timed reminders, then restores Off:

```powershell
.\tests\Invoke-TraySmoke.ps1 -ProcessId 12345
```

Use **Send a safe test notification** for native testing. Preview faces do not automatically save; do not press in-app Save on a synthetic preview in a real profile.

### Phase 3 manual notification checks

1. Inspect five icon actions and their tooltips, including keyboard/Narrator and light/dark/high-contrast Windows notification themes.
2. In an isolated synthetic profile, test immediate-save and preselection preferences separately. Dismiss and Skip must not add neutral or other records.
3. Send a safe preview, Quit RUOK, and activate its face from Notification Center. Confirm cold activation initializes storage and opens the matching preview without a real record.
4. Select the two-button layout, save, and send a preview. Verify Take the survey and Skip for now.
5. Check disabled Windows notifications and Do Not Disturb without overriding the user's settings.
6. Exercise real-time interval expiry, overnight/weekend windows, sleep/resume and idle return. Unit tests cover the policy but are not an OS delivery guarantee.
7. Quit from the tray and verify no RUOK process remains. Restart and check that settings persist; no sign-in startup task should be registered.

## Previously verified Phase 2 baseline

- Locked dependency restores succeeded for Debug and Release configurations.
- Debug and Release solution builds succeeded with zero warnings and zero errors.
- Both configurations passed all 172 MSTest cases: 142 domain and 30 infrastructure/application/export cases, with no failures or skipped cases.
- The actual x64 Release app launched successfully. Native UI smoke checks passed for all six screens, all five radio buttons' accessible names, and breathing start/pause/resume/restart/navigation-away pause/stop.
- Full one-minute completion, navigation after completion, restart after completion, and closing during an active exercise were exercised. The controlled close returned exit code zero.
- Compact 800 by 620 physical-pixel layouts kept mood choices within the window and primary actions visible. A 1520 by 980 layout changed breathing controls to a single visible row. Primary actions are pinned; secondary content scrolls.
- Light and Dark themes changed in the running process without Apply or restart, including the title bar. The automatically saved Light choice survived restart. Reduced motion toggled without Apply. Original appearance and motion preferences were restored after these checks.
- The first-use privacy prompt was observed. By the final smoke run it had already been acknowledged, so that script's first-use gate check was explicitly not run. The automated service tests independently verify consent-before-save and consent reset.

Validation exposed and corrected a runtime-only navigation icon conversion failure, an unsupported desktop high-contrast event subscription, and generic type names being announced for mood controls. The app now uses the Windows App SDK [ThemeSettings API](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.system.themesettings) for desktop high-contrast changes.

The UI smoke script did not create wellbeing entries or modify preferences. The separate appearance check temporarily changed presentation preferences and restored them. End-to-end save/export/delete UI checks and broad manual accessibility/scaling checks below are still outstanding.

### Breathing-exit follow-up

Managed deadline-pause and reentrant snapshot defects were reproduced and corrected. Timer/event handlers now detach before shutdown; stale native automation announcements are discarded.

The earlier native `0xc0000409` exit in `ucrtbase.dll` has **not been conclusively attributed**. The subsequent native completion and controlled-shutdown checks did not reproduce it. Those results do not prove the original native failure is eliminated; an exact reproducing action and scoped native diagnostics are needed if it recurs.

It recurred during normal `CloseMainWindow` requests at 2026-09-09 16:35:56 (prior Release PID 59200) and 16:44:18 (Phase 3 Debug PID 50496), with no managed .NET Runtime event. Inspection of the matching CRT identified the fault as `abort + 0x4e` / fast-fail code 7, not its caller. Another Debug process later exited with code zero. Notification/tray code is not necessary to trigger the earlier failure. Do not claim a resolved root cause or work around it with forced termination. The next diagnostic is a consented, per-executable crash dump in an isolated synthetic-data run; keep dumps only in ephemeral customer-data scratch storage, restore any diagnostic settings, and obtain the caller stack above `abort`.

## Automated tests

From the RUOK root, build and then run:

```powershell
dotnet build .\RUOK.slnx
dotnet test .\RUOK.slnx --no-build --no-restore
```

Domain tests cover model invariants, independent score denominators, range boundaries, time zones, daylight saving, and a monotonic breathing timeline with pause/resume/restart/stop.

Infrastructure/application tests cover real DPAPI round trips, corruption and failure injection, purpose binding, uniqueness, range deletion, retention, consent reset, cancellation, concurrent writes, CSV encoding/quoting/formula protection, accessible mood resources, and presentation updates that preserve consent, retention, and entries.

Temporary databases are unique per test and removed at test cleanup. The real application database is not used.

## Native UI smoke test

The XAML compiler does not detect every runtime conversion or desktop API incompatibility. The separate [UI smoke script](../tests/Invoke-UiSmoke.ps1) uses Windows' existing UI Automation support to exercise the actual packaged app.

Launch the app, finish or stop any breathing exercise, and find its process ID:

```powershell
Get-Process -Name 'RUOK.App' | Select-Object Id, MainWindowTitle
```

Run the following from the RUOK root, replacing `12345` with that process ID:

```powershell
.\tests\Invoke-UiSmoke.ps1 -ProcessId 12345
```

To include one-minute completion and compact-window layout checks:

```powershell
.\tests\Invoke-UiSmoke.ps1 -ProcessId 12345 -CheckCompletion -CheckCompactLayout
```

Add `-CheckFaceSelection` to verify each full-face radio and Skip without saving. This requires an empty draft and refuses to replace an existing mood selection.

For the focused window/navigation alignment regression:

```powershell
.\tests\Invoke-UiSmoke.ps1 -ProcessId 12345 -AlignmentOnly
```

This passed at 900, 1200 and 1600 physical-pixel window widths, before and after toggling the navigation pane (nine layouts). It checks that all five tiles remain present, equally wide, contained, centered within two physical pixels, and use at least 97% of the available row width. It also checks the collapsed Refresh button against the navigation toggle's center line. Original window dimensions and pane state are restored; data, reminder preferences and mood selections are not changed. The default radio layout and keyboard/selection semantics are retained.

Choose a one-minute exercise and keep RUOK active during the completion check. The compact check temporarily resizes the window and restores its original dimensions.

The script navigates all six screens, verifies the five radio buttons' accessible names, and exercises breathing start, pause, resume, restart, navigation-away pause, and stop. It leaves the app on the dashboard. It does not save check-ins, change consent, export, or delete data. The default run does not change preferences; `-CheckNebula` temporarily toggles and restores reduced motion as described above. It checks the first-use save gate only if the acknowledgement is still visible; otherwise it explicitly reports that check as not run.

This is not a substitute for Narrator, keyboard-only, high-contrast, scaling, or visual layout testing.

## Manual acceptance checks

These checks are required in addition to automated tests; a successful build does not establish accessibility or production readiness.

1. Open the app as a standard user. Confirm the privacy acknowledgement appears before the first save.
2. Navigate using only Tab, Shift+Tab, arrows, Enter, and Space. Check visible focus and no traps.
3. Select each mood. Confirm the text, numeric value, distinct face, and selected state.
4. Skip a draft. Verify that neither history nor the dashboard count increases.
5. Save a synthetic mood, with one optional score missing. Restart and confirm that the same entry persists and the missing score remains missing.
6. Try multiple factors, then Prefer not to say. Verify exclusivity.
7. Review 24-hour, week, month, all-data, and custom ranges. Invalid custom dates must not silently change the displayed range.
8. Export the displayed entries. Confirm sensitivity warning, cancellation, overwrite behavior, Unicode, and correct columns. Remove synthetic exports afterward.
9. Cancel and then confirm individual deletion, range deletion, shortened retention, and reset. External exports must not be deleted.
10. Select Light, Dark, and Follow Windows without Apply or restart. Confirm immediate content/title-bar changes and persistence after restart. Reduced motion should also save automatically. Changing presentation must not apply an unconfirmed retention selection.
11. Enable Windows high contrast and enlarged text. Inspect all content at 100%, 150%, and 200% display scaling and with increased text size.
12. Use Narrator: verify navigation names, five mood labels, errors, phase changes, and chart/history text. Countdown ticks must not continuously interrupt speech.
13. Start a breathing exercise; pause/resume/restart/stop. Switch pages or away from RUOK and verify pause. Confirm static mode still provides phase and time cues.
    - Confirm the nebula moves in Ready/Paused/Stopped/Completed states, expands on inhale, and contracts on exhale.
    - Toggle the Windows animation preference while RUOK is open; both kinds of motion should follow it without restarting.
    - In high contrast, verify a static, readable outline and unchanged accessible phase/time controls.
    - Scroll the entire orb offscreen, minimize, hide to the tray, and revisit the page. Animation should suspend/recover appropriately, without duplicate renderers or callbacks after Quit.
14. Inspect a narrow and a large window for clipping, excessive horizontal scrolling, and target sizes.
15. Disconnect networking after restoring dependencies and repeat the main flow. The app should work without online services.

## Remaining release gates

Clean-machine installation, certificate trust, broader native notification/environment coverage, sign-in startup, advanced accessibility audits, full performance profiling, native shutdown attribution, security/privacy review, update/uninstall behavior, ARM64 validation, and portable restore tests remain later-phase work.

The generic resource page is not a regional crisis directory. No emergency is inferred and no person or organization is contacted automatically.
