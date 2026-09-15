# RUOK

A local Windows wellbeing reflection app. **Version 1.0.0 is a preview, not a release-hardened product or a medical device.**

RUOK does not diagnose conditions, replace professional care, or infer emergencies from mood scores.

## Download and run

Download the Windows x64 EXE and its SHA-256 file from the [v1.0.0 release](https://github.com/WFy1337/RUOK/releases/tag/v1.0.0).

1. Save **RUOK-v1.0.0-win-x64.exe** in a permanent local folder.
2. Double-click it as your normal Windows user. Windows 11 24H2 (build 26100) or later, x64, is required. Visual Studio, Developer Mode, and a separately installed .NET/Windows App SDK are not needed by this distribution.
3. Read the privacy notice. Reminders and motivational notifications start **Off**; enable them explicitly in **Settings & privacy**.

The EXE includes its runtimes, original assets and third-party notices. It extracts dependencies to a per-user .NET cache on first launch; it is one downloadable file, not a zero-extraction binary. Keep its location stable because Windows notification activation refers to that EXE. Quit RUOK before replacing it with a newer download.

**Unsigned preview:** no code-signing certificate is configured. Windows/SmartScreen or organizational policy may warn or block execution. Verify the release checksum and follow your organization's policy; do not disable security protections.

The standalone app uses its own persistent local profile, separate from the packaged development app and RUOK Testing. It does not copy or migrate their data. See [Data and privacy](#data-and-privacy), the [validation record](docs/TESTING.md), and [release build instructions](docs/RELEASING.md).

## Implemented

- Native WinUI navigation and a personal dashboard.
- Five original facial icons with text labels, numeric values, and distinct color accents.
- Quick PulseChecks, optional stress/energy, optional factors, and Skip without recording a response.
- Explicit privacy acknowledgement before local recording.
- SQLite storage with user-bound DPAPI protection for payloads and preferences.
- History with 24-hour, 7-day, 30-day, all-data, and inclusive custom-date filters.
- A mood distribution chart with readable labels/counts and corresponding accessible history.
- Individual/range deletion, complete local reset, and configurable retention.
- Explicit CSV export of the displayed entries, with a sensitivity warning.
- Adjustable inhale/exhale exercise with pause, resume, restart, stop, and static mode.
- An original animated nebula breathing orb with drifting cloud layers, star dust, a soft glow, and breathing-synchronized expansion.
- Immediate light, dark, and Windows-system appearance, including title-bar theming and automatic preference saving.
- Automatic reduced-motion preference saving; retention still requires an explicit Apply action and confirmation when shortened.
- Responsive mood/card/button layouts, compact navigation, and pinned check-in and breathing actions.
- English resource strings separated for future localization.
- Full-face radio tiles without separate radio circles; equal-width tiles resize and stay centered as the window/navigation pane changes.
- Centered compact-sidebar Refresh control and aligned, wrapping factor labels.
- Opt-in native Windows reminders with five face actions, or Take the survey / Skip for now.
- Immediate local mood saving from real notification faces, with a setting to open a preselected PulseCheck instead.
- Configurable reminder interval, local-time window, weekday restriction, and optional active/idle-aware timing.
- Tray-on-close while reminders are enabled, a tray Quit action, and single-instance reopening.
- Safe preview notifications whose actions never automatically record wellbeing data.
- Optional motivational notifications with random short messages, adjustable spacing, a custom message list and a safe preview.
- Domain, application, persistence, encryption, export, and accessibility-resource tests.

No sign-in startup registration, activity history, telemetry, AI, online account, employee reporting, or network requests are implemented.

## Source history

The project is versioned in the [WFy1337/RUOK repository](https://github.com/WFy1337/RUOK). The default branch is `main`. The `phase3-nebula-checkpoint` tag preserves the original Phase 3 preview, including notifications, responsive face selection, and the animated breathing orb. The main-only motivational notification addition does not import features from `feature/major-update-testing`.

The repository contains source, original assets, tests, documentation, and dependency lock files. Build output, local wellbeing databases/exports, credentials, signing keys, crash dumps, and local agent state are excluded. This is a **source-code checkpoint, not a backup of personal wellbeing data**.

For future updates, run the checks relevant to the change, review the staged files, then commit and push from the RUOK directory:

```powershell
git status --short
git add --all
git diff --cached --stat
git commit -m "Describe the validated change"
git push
```

Only commit intentional project changes. Review the staged diff when changing configuration or handling diagnostic files; ignore rules are not a substitute for checking for secrets. A local commit is saved on GitHub only after a successful push.

When separate main/testing worktrees are present, verify the current folder and branch before building or committing. Do not switch the dirty testing worktree to main or copy its pending changes. Obtain the owner's approval before each GitHub push.

## Breathing visual

The nebula gently moves whenever the breathing page is visible, including before starting, while paused, and after stopping or completing an exercise. Inhale expands the orb; exhale contracts it. Pausing freezes the breathing size and clock, not the ambient cloud movement.

- **Static breathing visual / reduced motion** stops both kinds of animation while retaining the nebula artwork and the phase/time cues. Windows animation preferences are also respected, including changes while RUOK is running.
- High contrast uses a static, theme-aware concentric outline instead of the colored nebula. Phase text remains the accessible cue; decorative frames are not announced.
- Animation pauses when the window is hidden/minimized or the orb control is outside the scrolling viewport. Leaving the page releases its composition resources; returning recreates them.
- Native compositor animations drive the clouds, dust, and glow. The existing exercise timer still runs only during an active exercise; no always-running UI animation timer or per-frame texture generation was added.
- The two cloud masks are original, locally generated artwork. Regenerate them with [New-NebulaAssets.ps1](scripts/New-NebulaAssets.ps1). No external imagery, network dependency, or additional rendering package is used.

## Reminders

In **Settings & privacy > Notifications and Auto mode**, choose your timing and select **Save reminder settings**.

- Defaults: Off, 90-minute interval, 09:00-17:00 local time, weekdays only, five face buttons, and immediate saving when a real notification face is clicked.
- The interval can be 15-480 whole minutes. Saving the schedule starts a fresh interval; a recorded check-in postpones the next reminder. A missed interval never creates a burst of reminders.
- Overnight windows are supported and belong to their starting weekday. Windows time-zone changes require restarting RUOK, as with the history view.
- Auto mode is an explicit opt-in. It reads only the current Windows session's idle duration using [GetLastInputInfo](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getlastinputinfo). It waits at five minutes idle, then allows two minutes after return. The activity state is transient; no keys, pointer locations, app names, window titles, or activity history are recorded.
- Close keeps the process in the tray while either PulseCheck reminders or motivational notifications are enabled and the tray preference is selected. Use **Open RUOK** or **Quit RUOK** in the tray menu. Launching again also restores the existing window.
- Quit stops reminder delivery. There is no startup task, Windows background service, OS scheduled-toast queue, or wake timer. Open RUOK again after sign-in or Quit.
- Windows controls the banner's corner placement, dimensions, duration, sound, notification-center behavior, and Do Not Disturb. RUOK does not bypass those settings. Five icon actions leave no sixth custom Skip button; normal dismissal records nothing.
- **Send a safe test notification** uses the saved layout. A test face opens a preview, never an automatic save; use in-app Skip afterward to clear the preview. Normal manual Save remains a real recording action.
- Turning reminders off or resetting clears RUOK's notification group. Stale, invalid, disabled, and unconsented save requests are refused. Repeated activation of a retained notification entry does not add or overwrite a record.

The implementation follows Microsoft's [notification content guidance](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content), [activation quickstart](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart), and [single-instance guidance](https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance). Notification assets are original; regenerate their scale/contrast variants with [New-NotificationIcons.ps1](scripts/New-NotificationIcons.ps1).

## Motivational notifications on main

Open **Settings & privacy > Motivational notifications**.

1. Use **Send a preview now** to demonstrate a random message immediately. Preview uses the editor, even while automatic delivery is Off, without saving edits or enabling a schedule.
2. Turn on **Send motivational notifications automatically**, choose the minimum spacing, then select **Save encouragements**. The default spacing is 120 minutes, giving a random delay of 2-4 hours. The supported minimum is 15-240 whole minutes; each actual delay is between that value and twice it.
3. Edit **Your messages - one per line** to replace or add messages. Use 1-30 distinct messages, up to 160 characters each. Blank lines and case-insensitive duplicates are ignored. **Use default messages** restores the original list in the editor; Save applies it.
4. To stop, turn the switch Off and select **Save encouragements**. This clears the pending schedule and encouragement notifications without changing PulseCheck preferences.

Automatic encouragements default to **Off**, including existing installations. They use the saved notification hours and weekday selection in the section above, even if PulseCheck timing is Off. They do not require or record a mood, use wellbeing history, infer progress, or respond to scores. Built-in messages are original short encouragements such as "Keep it going!", "Small steps count." and "Your effort matters." The previous selected message is avoided when the list contains more than one distinct message.

The next attempt is protected and saved before Windows delivery. Restart/sleep recovery considers one overdue message during an allowed window, then schedules a new random delay; it never replays a backlog. PulseCheck and encouragement delivery are spaced by at least a minute when both channels are in use. Opening an encouragement only opens the dashboard, never a preselected or saved check-in.

Leave RUOK open or running in its tray. Quit, sign-out and PC sleep stop in-process delivery; no new startup service or wake task is installed. Windows notification permissions and Do Not Disturb still control visibility. A notification expires after one hour. Custom text is protected in RUOK's local storage, but Windows displays and stores the notification separately: do not put sensitive information in the message list.

This feature uses a separate protected row in the existing Preferences table. SQLite remains schema 1; existing check-in records and the original `app` preference payload are unchanged. The pre-encouragement Phase 3 binary ignores this additional row. Full reset clears the extension with all other local data; CSV remains check-in-only. See [DATA-DICTIONARY.md](docs/DATA-DICTIONARY.md) and [TESTING.md](docs/TESTING.md).

## Development prerequisites

- Windows 11 24H2 or later on a supported servicing channel; x64 is the current development target.
- .NET SDK 10.0.401, or a later patch in its feature band, selected by [global.json](global.json).
- Official WinUI project templates and Developer Mode for the template's debug identity workflow.
- VS Code C# tooling; C# Dev Kit is recommended.

The initial project was created using Microsoft's [WinUI command-line quickstart](https://learn.microsoft.com/windows/apps/get-started/start-here). Do not regenerate it over these source files.

## Build, test, and run

Open the **RUOK folder itself** in VS Code so its tasks are discovered. Run these commands from that folder.

Restore the exact recorded dependency graph, then compile:

```powershell
dotnet restore .\RUOK.slnx --locked-mode
dotnet build .\RUOK.slnx --no-restore
```

Run the existing tests after the build:

```powershell
dotnet test .\RUOK.slnx --no-build --no-restore
```

Launch the local desktop app:

```powershell
dotnet run --project .\src\RUOK.App\RUOK.App.csproj --no-build
```

The equivalent VS Code tasks are **RUOK: Build**, **RUOK: Test**, and **RUOK: Run**. Close a running build of RUOK before recompiling if Windows reports locked output files.

For a Release compilation:

```powershell
dotnet restore .\RUOK.slnx --locked-mode -p:Configuration=Release
dotnet build .\RUOK.slnx --configuration Release --no-restore
```

The configuration-specific restore downloads the Release runtime pack when it is not already cached. See the [validation guide](docs/TESTING.md) for verified results and the native UI smoke test.

A normal Release compilation is not the downloadable distribution or a signing workflow. To build the standalone EXE, use [Publish-Standalone.ps1](scripts/Publish-Standalone.ps1) as described in [RELEASING.md](docs/RELEASING.md). Trimming remains disabled.

## Dependencies

Exact direct versions are in the project files; transitive versions are recorded in their package lock files.

| Dependency | Version |
|---|---|
| Microsoft.WindowsAppSDK | 2.4.0 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 |
| Microsoft.Windows.SDK.BuildTools.WinApp | 0.6.1 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.DependencyInjection | 10.0.12 |
| Microsoft.Data.Sqlite | 10.0.12 |
| System.Security.Cryptography.ProtectedData | 10.0.12 |
| MSTest | 4.0.2 |

WinApp tooling remains part of the packaged development workflow. Standalone publishing has a separate dependency lock and output trees; all existing resolved dependency versions are retained. Code signing and clean-machine certification remain future work.

## Architecture

- [RUOK.Domain](src/RUOK.Domain): immutable validated entries, transparent aggregation, monotonic breathing timeline.
- [RUOK.Application](src/RUOK.Application): consent-before-recording, retention, data operations, local-date conversion, reminder policy, and validated notification intents/payloads.
- [RUOK.Infrastructure](src/RUOK.Infrastructure): protected SQLite persistence and safe CSV writing.
- [RUOK.App](src/RUOK.App): WinUI views, view models, dialogs, native accessibility, and composition root.
- [Tests](tests): synthetic data only, isolated temporary stores, and mocked failures.

SQLite I/O is synchronous, so the adapter serializes operations on a worker rather than blocking the UI. No mood or timestamp SQL indices contain plaintext wellbeing data.

## Data and privacy

The app displays its resolved database path in **Settings & privacy**. Packaged local storage normally resolves beneath:

```text
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\Data\ruok.db
```

The standalone release instead uses:

```text
%LOCALAPPDATA%\RUOK\Standalone\Data\ruok.db
```

Its database remains in that location when the EXE is moved or replaced. Both distributions use the same schema and protection rules but separate profiles and single-instance keys. Neither reads the testing profile.

The table structure, opaque entry identifiers, ciphertext sizes, and row counts are visible. This is **encrypted payload storage**, not whole-file database encryption.

DPAPI protection is tied to the Windows user context. Losing the Windows profile can make the data unrecoverable. A copied database is not a portable backup. No portable backup/restore is implemented yet.

The default retention is 365 elapsed days. Removal occurs on launch or refresh, not while RUOK is closed. Options are 30, 90, 365 days, or until manually deleted. Shortening retention requires confirmation.

Reset removes local records and preferences, disables reminders, clears RUOK's notification group, and requires privacy acknowledgement again. It does not remove exports, external backups, or forensic disk traces. A normal package uninstall is not a guarantee of secure erasure.

Notification payloads are managed by Windows, not the encrypted SQLite store. They contain generic prompts, all offered face choices, a random reminder ID and its creation time; they do not contain previous wellbeing entries, trends, factors, stress, or energy.

See [the data dictionary](docs/DATA-DICTIONARY.md) and [validation checklist](docs/TESTING.md).

## Known boundaries and next phases

- The window has DPI-aware minimum bounds of 600 by 440 effective pixels. Smaller layouts scroll secondary content while primary actions remain visible.
- One non-clinical breathing exercise; no sound or vibration yet.
- No notes, detailed State of Mind questionnaire, or automatic interpretation.
- History includes stress/energy values; advanced stress/energy charts and comparisons remain Phase 4.
- No XLSX, JSON portability export, portable backups, scheduled themes, or configurable resource contacts yet.
- Sign-in startup remains a later opt-in feature. This build never registers or enables it.
- A previously observed native shutdown failure remains under investigation; see [the validation guide](docs/TESTING.md). Do not treat this preview as release-hardened.
- Security/privacy review, broad accessibility/performance testing, signed installers, update/uninstall testing, and ARM64 validation remain Phase 5.

## Troubleshooting

- **Downloaded EXE fails to launch:** verify Windows/x64 requirements, the release checksum and organizational application-control policy. Do not copy a development build's EXE by itself; use the published standalone asset.
- **Development build fails to launch:** build from this directory, inspect compiler output, and verify Developer Mode and the template's package identity registration.
- **Storage error:** check disk space, file permissions, and the current Windows profile. Refresh rather than deleting the database. RUOK does not silently reset corrupt or undecryptable data.
- **Unrecognized schema:** use the compatible RUOK version; do not overwrite the database.
- **CSV failure:** check destination permissions, free space, and whether another application holds the file open. Failed/cancelled writes do not deliberately replace an existing export.
- **Task not found in VS Code:** open this RUOK folder, not just its parent workspace.
- **No notification banner:** use the safe test, check Windows Notification Center, Do Not Disturb, and Settings > System > Notifications. Run RUOK normally, not elevated. A successful Show only means Windows accepted the notification.
