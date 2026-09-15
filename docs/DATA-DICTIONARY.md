# Phase 3 data dictionary

## Distribution and storage separation

The standalone 1.0.0 release preserves the schema and entry semantics below. It uses the current user's Local AppData **RUOK\Standalone\Data\ruok.db** rather than the packaged app's **Packages\<PackageFamilyName>\LocalState\Data\ruok.db**. The two profiles are independent; neither reads the experimental testing profile.

Only application/runtime files and third-party notices are bundled in the EXE. No database, preferences, CSV exports or user content is included, and no automatic profile migration is performed.

## Entry semantics

A PulseCheck is recorded only after an explicit in-app Save or an explicitly clicked real notification face configured for immediate saving, and only with persisted privacy acknowledgement. Skipped questions remain missing, never neutral or zero. Optional fields do not change the mood score.

A notification face saves a Quick entry containing only mood, the activation-time timestamp and the local time-zone identifier. Its random notification identifier becomes the entry identifier, so duplicate activation cannot add or overwrite that retained entry. Open/preselect actions and test notifications do not automatically save. Notification actions older than 24 hours, more than five minutes in the future, malformed, or disabled by current preferences are not saved.

Mood: 1 = Very low, 2 = Low, 3 = Neutral, 4 = Good, 5 = Great.

Stress: 1 = Very little stress through 5 = Very high stress. Higher stress is not interpreted as a better outcome.

Energy: 1 = Very little energy through 5 = Very high energy.

Factors are optional, unique stable identifiers: Work, Family, Health, Finances, Sleep, SocialActivity, Exercise, CurrentTask, Other, PreferNotToSay. PreferNotToSay excludes all other factors.

There is no patient/employee identifier, employer identifier, medical score, note, or productivity field.

## CSV schema version 1

Exports contain only the currently displayed history range, ordered chronologically. CSV files are unencrypted UTF-8 with a BOM, CRLF record endings, comma delimiters, and quoted cells. Empty optional values mean not recorded.

| Field | Representation |
|---|---|
| entry_id | Random 32-character GUID representation; pseudonymous, not anonymous |
| timestamp_local | ISO 8601 timestamp with the original offset at recording |
| timestamp_utc | Same instant expressed with UTC offset +00:00 |
| utc_offset_minutes | Signed integer offset in minutes, e.g. -240 |
| time_zone_id | Windows time-zone identifier at recording |
| pulsecheck_type | Quick |
| mood_score | Integer 1-5 |
| mood_label | Stable identifier: VeryLow, Low, Neutral, Good, Great |
| stress_score | Integer 1-5, or empty |
| energy_score | Integer 1-5, or empty |
| influencing_factors | Semicolon-separated stable identifiers, or empty |
| schema_version | 1 |

Free-form time-zone text is protected against spreadsheet formula interpretation by an apostrophe prefix when needed. Quotes, commas, newlines, and Unicode are escaped/preserved. An exported file can still identify its owner through its content and timestamps.

## Calculations and date interpretation

- Mood average = sum of recorded mood scores / number of included responses.
- Optional stress and energy averages use only entries with that field recorded, each with its own count.
- Distribution always includes all five moods, including zero-count bins.
- Daily bins without entries have count zero and no average.
- No values are imputed, smoothed into missing days, or used to diagnose a condition.
- The dashboard uses the last seven local calendar days, including today.
- History's last 24 hours is an elapsed-time range; 7/30 days are local calendar ranges.
- Custom dates include both selected days, converted to a start-inclusive/end-exclusive UTC interval.
- Daylight-saving changes can produce 23- or 25-hour local days.
- Displayed dates use the local zone captured when RUOK starts; original recording offsets are preserved in storage/export. Restart after changing the Windows time zone.

## On-disk schema version 1

SQLite `user_version` identifies the database schema.

- Entries: entry_id (primary key), protected_payload (BLOB), protection_version.
- Preferences: key (primary key), protected_value (BLOB), value_version.

Payloads contain a versioned JSON envelope protected with Windows DPAPI CurrentUser before reaching SQLite. The row identifier is part of the protection purpose. Corrupt data, wrong-purpose ciphertext, and unsupported schema versions produce explicit errors rather than empty-history fallbacks.

Database payloads are not a portable backup format. Backup/restore and JSON/XLSX portability are deferred.

## Notification preferences

The existing DPAPI-protected `app` preference envelope now also contains:

- `notifications`: mode (Disabled, Timed, ActivityAware), interval minutes, local window start/end, weekday restriction, tray preference, face action (SaveMood or OpenPulseCheck), and layout (Faces or SurveyAndSkip).
- `nextReminderUtc`: next interval threshold, or missing/null when not scheduled.

No input events or idle samples are persisted. Windows owns notification-center payload storage separately from RUOK's encrypted database. Those payloads contain generic prompt text, offered actions, a random notification identifier and creation time, not saved wellbeing history.

The SQLite schema and CSV schema remain version 1. Older preference documents acquire safe defaults (reminders Off). Because unknown JSON members are deliberately rejected, application binaries predating these original notification fields may reject this `app` payload; do not reset data to work around that error.

## Main-only motivational notification extension

Encouragements use the **existing** Preferences table with the separate key `encouragements`, value version 1 and protection purpose `RUOK:encouragements:v1`. The existing versioned DPAPI envelope and size limit apply. No new table or schema migration is introduced, and no fields are added to the original `AppSettings` JSON.

The protected value contains:

| Field | Meaning |
|---|---|
| preferences.enabled | Explicit automatic-delivery choice; defaults false |
| preferences.minimumIntervalMinutes | Whole minutes, 15-240; default 120 |
| preferences.messages | 1-30 distinct nonempty message lines, each at most 160 UTF-16 code units; up to 4,860 code units for the editor text |
| nextUtc | UTC threshold for the next attempt; null when disabled or not yet initialized |
| lastMessage | Previously selected message, used to avoid immediate repetition; null when disabled or the message list is replaced |

Messages are trimmed, case-insensitive duplicates are removed, and line endings normalize to LF on save. Control characters within a message and invalid XML characters are rejected. A single-message list necessarily repeats that message. The built-in English list contains original generic encouragements, not statements inferred from check-ins.

Each next delay is uniformly selected in whole seconds between the minimum spacing and twice that spacing, inclusive. Display is restricted by the existing saved PulseCheck hours/weekday fields, independently of PulseCheck mode or wellbeing-recording consent. The existing local-zone-at-startup policy applies. One overdue message may be considered on return to an allowed window; the next threshold is then calculated from the current attempt, not from an old backlog.

An immediate SQLite transaction compares the entire expected encouragement state before replacing it. The schedule advances before native Show, so concurrent/restarted callers cannot replay an already-reserved attempt. This is not a delivery receipt: Windows rejection, interruption or a crash may lose that message, and no completion or mood is inferred. Failed writes do not authorize delivery; repeated compare-and-swap conflicts surface an error.

The Windows group `RUOK.Encourage` is separate from `RUOK.CheckIn`, with `message` and `test` tags and a one-hour native expiration. Launch arguments contain only the existing v1 opaque identifier/time/test flag plus `action=encouragement`, never the custom message. That action only opens the dashboard and cannot authorize a PulseEntry, including if a mood is forged into an in-memory intent. Mood arguments on the wire are rejected for this action.

Preview reads the editor and does not persist messages, advance the automatic schedule or enable notifications. Saving Off clears the pending threshold and prior-message marker. Replacing the list clears its old marker; full reset deletes the entire row. Normal check-in deletion, retention, appearance changes and legacy preference saves leave it intact. CSV does not include messages or schedule settings.

The pre-encouragement Phase 3 application reads only the `app` row and therefore ignores this extension without an unknown-JSON-member error. A damaged extension raises an explicit error and disables its controls; the existing check-in UI remains usable. Windows retains notification text separately from the DPAPI database, so custom messages must not contain sensitive information.
