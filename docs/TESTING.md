# RUOK validation

Use synthetic values, not real wellbeing records, when testing.

## Standalone 1.0.0 release verification

The standalone distribution was built from the separate stable `main` worktree. Experimental source, package registration and testing-database metadata were verified unchanged.

### Automated and build checks

- Normal packaged Debug and Release solution builds succeeded with **zero warnings/errors**.
- Full suites passed **274 cases per configuration: 142 Domain + 132 Infrastructure/Application/Export**. The final runtime/helper changes were also rerun in the focused Debug selection: **102/102** notification, encouragement and runtime cases.
- Eleven packaging cases cover independent unpackaged storage/instance naming, application-relative asset paths, file URIs, five accessible native actions, URI escaping, and rejection of remote/relative/UNC images or invalid image counts.
- Standalone restore is locked. Its graph retains every previously resolved dependency version; the existing package locks remain unchanged.
- The publisher produced exactly one self-contained EXE, without PDB or runtime sidecars, plus the separately generated SHA-256 file. Upstream notices, including .NET runtime terms and SQLitePCLRaw copyright/license declarations, were verified.
- A clean-commit build is required again after committing release source. The published checksum and source commit must describe that final artifact, not an intermediate development build.

### Native checks outside the source tree

Only the EXE was copied to a separate directory with spaces. It was launched with **C:\Windows** as the working directory, invalid process-local `DOTNET_ROOT`/`DOTNET_ROOT_X64` locations, and multilevel .NET lookup disabled. No runtime package was installed, removed or globally reconfigured.

- The renamed release EXE opened independently of the already-running packaged main app and used the separate standalone profile. Native WinUI/runtime modules and assets were loaded from the extracted bundle.
- First-use acknowledgement was exercised only in the newly created standalone test profile.
- All original pages, full-face selection/Skip, compact primary actions, face centering and compact Refresh alignment passed.
- The nebula animated in ready, paused, stopped and reloaded states; breathing changed its observed diameter from **179 to 234 pixels**. Reduced motion and offscreen/minimize suspension passed. Frames stayed in memory.
- Windows registration succeeded after the required runtime resource was bundled and explicitly loaded. Safe encouragement and five-face test payloads were accepted by Windows.
- Message editing, save/reload, spacing, preview while Off, disabling and explicit default restoration passed. Unlike the earlier packaged pass, **encouragement-only tray Close/reopen was verified**, preserving the same process while PulseCheck reminders stayed Off.
- The test profile contained **no retained check-ins**. Its temporary preferences and acknowledgement were reset through the application's confirmation flow, returning it to first-use state. The packaged main profile was not reset, copied or reconfigured.

### Packaging defects caught before release

1. An executable-named PRI caused renamed downloads to fail in native WinUI. Using **resources.pri** corrected startup without adding a custom resource-manager override.
2. The pinned SDK's self-contained inputs omitted **Microsoft.WindowsAppRuntime.Insights.Resource.dll**. Native registration returned `0x8007007E`; restricted error details identified that exact DLL. The same-version x64 runtime MSIX contains it. The native runtime DLL's hash matched the installed reference package during diagnosis. The publisher now extracts the resource from the locked NuGet archive, not from WindowsApps.
3. Merely bundling the resource was insufficient for native lookup. Loading it by its absolute extracted path before registration resolved the error without modifying global DLL search paths. Temporary startup/error tracing was removed.

### Remaining coverage limits

Windows accepted both notification types, but Windows shell automation did not expose the specific preview for a physical button/body click. **Banner image rendering and cold notification-body activation are not claimed as verified.** Protocol/read-only behavior is covered by automated tests; normal launch and warm tray reopening were exercised.

Clean-machine/VM, ARM64, signing, broad accessibility/performance and updater/uninstaller certification remain outstanding. The earlier intermittent native shutdown fault is still tracked: some controlled exits succeeded, but that does not establish a fix. The distribution remains an unsigned preview.

Build/publish instructions and source references are in [RELEASING.md](RELEASING.md).

## Main-only motivational notifications

Implemented from the original stable `main` checkpoint in a separate `RUOK-main` worktree. No source or feature from `feature/major-update-testing` was copied or merged. The original application identity, SQLite schema 1, check-in features, entry-only CSV and dependency versions remain unchanged.

### Build and automated verification

- The original notification baseline passed **41/41** cases before changes.
- Added **50** encouragement cases. The combined new/original notification selection passed **91/91**.
- Complete Debug and Release solution builds passed with **zero warnings and zero errors**. Each configuration passed **263 tests: 142 Domain + 121 Infrastructure/Application/Export**, with no failures or skips.
- Coverage includes message bounds/Unicode/normalization, random selection without immediate repeats, spacing endpoints, shared hours/weekdays/overnight zones, delayed opt-in, restart and one-message recovery, disable/list replacement, separate protected persistence, unchanged legacy ciphertext/schema, concurrent CAS, cancellation, corruption/purpose rejection, bounded conflicts, failed persistence and read-only native arguments.
- Existing `AppSettings` JSON was not extended. The extra protected preference row is ignored by the pre-encouragement Phase 3 reader; tests verify that old settings writes preserve the extension and extension writes preserve the old settings/entry ciphertext.
- The editor did not discover tests or the build task in the new child worktree, so the existing CLI commands were authoritative. Missing app and Domain-test assets were restored only after `NETSDK1004`, using the existing lock files. No package version or lock file changed.

### Native verification

The final x64 Release app launched as **RUOK**, PID **22400**, from the main worktree under the unchanged production application identity. Existing data was not reset or copied from the testing installation.

- The saved PulseCheck mode was **Timed reminders**, not Off. It was preserved throughout native checks. Automatic encouragements were Off before testing and restored to Off afterward, with the original message list and 120-minute minimum spacing.
- The new controls remained horizontally contained at requested widths **800 and 1180 pixels**.
- Custom messages and minimum spacing saved and reloaded correctly. Windows accepted an encouragement preview while automatic encouragements stayed Off. Enabling/disabling, editor-only default restoration and final preference restoration passed.
- Tray Close/reopen retained the same live process. **Encouragement-only tray isolation was not asserted** because the existing active PulseCheck schedule was intentionally not disabled.
- Native-driver refinements handled minimized windows, NumberBox's range-value provider, WinUI's CR-only multiline value representation, and explicit Settings navigation after tray reopening. Failed intermediate checks either stopped before editing preferences or restored them in `finally`; the complete final run passed.
- Existing dashboard, PulseCheck, history, settings, about and breathing transitions passed. All five full-face choices were individually selected and cleared through Skip without saving.
- Original face centering/equal widths and compact Refresh alignment passed at **900, 1200 and 1600 pixels**, including both pane states. Compact mood choices and pinned actions passed at **800 x 620**.
- The existing nebula passed ready/paused/stopped/reloaded animation, breathing expansion (**180 to 234 pixels**), static mode and offscreen/minimize suspension at a normal **1180 x 920** viewport on the tested desktop. Smaller framing clipped the visual and overly large framing included changing configuration controls in the old helper's screenshot region; the correctly framed rerun passed without changing the breathing implementation or its helper. Frames stayed in memory; original motion and window settings were restored.
- First-use consent was not rerun because it was already acknowledged. No native check saved/deleted check-ins, accepted consent, exported data, changed retention, reset data or changed the existing PulseCheck timing.

Windows acceptance is not proof of banner visibility or a physical notification-body click. Automatic timing and read-only activation are covered by the tests; this pass did not wait for a real 15-minute-or-longer automatic interval or automate a Windows notification-body click. Windows notification permissions/Do Not Disturb, app/tray lifetime and awake-PC requirements still apply. The pre-existing intermittent shutdown issue remains outside this feature; the main app is left running.

### Reproducible commands

Run from the main worktree, never the dirty testing worktree. Use the current stable RUOK PID for native checks.

```powershell
dotnet test .\tests\RUOK.Infrastructure.Tests\RUOK.Infrastructure.Tests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~EncouragementTests|FullyQualifiedName~NotificationTests"
dotnet build .\RUOK.slnx --configuration Debug --no-restore
dotnet test .\RUOK.slnx --configuration Debug --no-build --no-restore
dotnet build .\RUOK.slnx --configuration Release --no-restore
dotnet test .\RUOK.slnx --configuration Release --no-build --no-restore
dotnet run --project .\src\RUOK.App\RUOK.App.csproj --configuration Release --no-build --no-restore --no-launch-profile
.\tests\Invoke-UiSmoke.ps1 -ProcessId 22400 -EncouragementsOnly
.\tests\Invoke-UiSmoke.ps1 -ProcessId 22400 -CheckFaceSelection -CheckCompactLayout -CheckNebula
.\tests\Invoke-UiSmoke.ps1 -ProcessId 22400 -AlignmentOnly
```

The encouragement helper requires saved encouragements Off and Keep in tray selected; finish draft edits first. It preserves the original PulseCheck mode, temporarily exercises only encouragement settings, and restores them. The optional existing nebula helper requires RUOK foreground/unobscured and a viewport that includes the full orb but not its configuration controls; use the tested normal-window dimensions above at the same DPI.

### Changed file inventory

| Surface | Files |
|---|---|
| Preferences, scheduling and store contract | [EncouragementPreferences.cs](../src/RUOK.Application/EncouragementPreferences.cs), [EncouragementService.cs](../src/RUOK.Application/EncouragementService.cs) |
| Read-only activation and escaped payload | [NotificationIntent.cs](../src/RUOK.Application/NotificationIntent.cs), [NotificationPayload.cs](../src/RUOK.Application/NotificationPayload.cs) |
| Separate protected preference row and CAS | [SqliteWellbeingStore.cs](../src/RUOK.Infrastructure/SqliteWellbeingStore.cs) |
| Native notification group and dependency/tray wiring | [WindowsNotifications.cs](../src/RUOK.App/Services/WindowsNotifications.cs), [MainWindow.xaml.cs](../src/RUOK.App/MainWindow.xaml.cs) |
| Viewmodel initialization, commands and timer integration | [MainViewModel.cs](../src/RUOK.App/ViewModels/MainViewModel.cs), [MainViewModel.Notifications.cs](../src/RUOK.App/ViewModels/MainViewModel.Notifications.cs), [MainViewModel.Encouragements.cs](../src/RUOK.App/ViewModels/MainViewModel.Encouragements.cs) |
| Settings and localized copy | [SettingsView.xaml](../src/RUOK.App/Views/SettingsView.xaml), [UiStrings.resx](../src/RUOK.App/Resources/UiStrings.resx) |
| Automated/native verification | [EncouragementTests.cs](../tests/RUOK.Infrastructure.Tests/EncouragementTests.cs), [Invoke-UiSmoke.ps1](../tests/Invoke-UiSmoke.ps1), [Encouragement-VisualChecks.ps1](../tests/Encouragement-VisualChecks.ps1) |
| Documentation | [README.md](../README.md), [DATA-DICTIONARY.md](DATA-DICTIONARY.md), [TESTING.md](TESTING.md) |

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
