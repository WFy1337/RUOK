# Phase 3 data dictionary

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

The SQLite schema and CSV schema remain version 1. Older preference documents acquire safe defaults (reminders Off). Because unknown JSON members are deliberately rejected, old application binaries may reject preferences saved by this version; do not downgrade over the same profile or reset data to work around that error.
