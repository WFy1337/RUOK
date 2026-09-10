# RUOK

A local Windows wellbeing reflection app. **This is the Phase 3 preview, not a production release or a medical device.**

RUOK does not diagnose conditions, replace professional care, or infer emergencies from mood scores.

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
- Domain, application, persistence, encryption, export, and accessibility-resource tests.

No sign-in startup registration, activity history, telemetry, AI, online account, employee reporting, or network requests are implemented.

## Source history

The project is versioned in the private [WFy1337/RUOK repository](https://github.com/WFy1337/RUOK). The default branch is `main`. The `phase3-nebula-checkpoint` tag preserves the current Phase 3 preview, including notifications, responsive face selection, and the animated breathing orb.

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
- Close keeps the process in the tray only while reminders and the tray preference are enabled. Use **Open RUOK** or **Quit RUOK** in the tray menu. Launching again also restores the existing window.
- Quit stops reminder delivery. There is no startup task, Windows background service, OS scheduled-toast queue, or wake timer. Open RUOK again after sign-in or Quit.
- Windows controls the banner's corner placement, dimensions, duration, sound, notification-center behavior, and Do Not Disturb. RUOK does not bypass those settings. Five icon actions leave no sixth custom Skip button; normal dismissal records nothing.
- **Send a safe test notification** uses the saved layout. A test face opens a preview, never an automatic save; use in-app Skip afterward to clear the preview. Normal manual Save remains a real recording action.
- Turning reminders off or resetting clears RUOK's notification group. Stale, invalid, disabled, and unconsented save requests are refused. Repeated activation of a retained notification entry does not add or overwrite a record.

The implementation follows Microsoft's [notification content guidance](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content), [activation quickstart](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart), and [single-instance guidance](https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance). Notification assets are original; regenerate their scale/contrast variants with [New-NotificationIcons.ps1](scripts/New-NotificationIcons.ps1).

## Prerequisites

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

Release compilation is not an installer or a signing workflow. Trimming is disabled until runtime/resource/serialization behavior is validated with trimming in a later release-hardening milestone.

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

WinApp tooling is used by the existing template for development identity. A stable production packaging/signing path and clean-machine dependency checks remain Phase 5 work.

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

- **Blank window or failed launch:** build from this directory, inspect compiler output, and verify Developer Mode and the template's package identity registration.
- **Storage error:** check disk space, file permissions, and the current Windows profile. Refresh rather than deleting the database. RUOK does not silently reset corrupt or undecryptable data.
- **Unrecognized schema:** use the compatible RUOK version; do not overwrite the database.
- **CSV failure:** check destination permissions, free space, and whether another application holds the file open. Failed/cancelled writes do not deliberately replace an existing export.
- **Task not found in VS Code:** open this RUOK folder, not just its parent workspace.
- **No notification banner:** use the safe test, check Windows Notification Center, Do Not Disturb, and Settings > System > Notifications. Run RUOK normally, not elevated. A successful Show only means Windows accepted the notification.
