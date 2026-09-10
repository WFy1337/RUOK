#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][int]$ProcessId)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class RuokTraySmoke {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
$app = Get-Process -Id $ProcessId
if ($app.ProcessName -ne 'RUOK.App' -or $app.MainWindowHandle -eq 0) {
    throw 'Supply the process ID of a visible RUOK window.'
}
$handle = $app.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$scope = [System.Windows.Automation.TreeScope]::Descendants
$text = @{}
[xml]$document = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\RUOK.App\Resources\UiStrings.resx') -Raw
foreach ($entry in $document.root.data) { $text[[string]$entry.name] = [string]$entry.value }
function Find-Name([string]$Name) {
    $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name))
}
function Find-Id([string]$Id) {
    $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Select-Mode([string]$Label) {
    $combo = Find-Id 'ReminderModeChoice'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Find-Name $Label).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 200
    (Find-Id 'SaveReminders').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
}
function Reopen-Ruok {
    Start-Process -FilePath explorer.exe -ArgumentList 'shell:AppsFolder\F8FF2497-B789-464A-9326-7774176A2B0F_1z32rh13vfry6!App'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not [RuokTraySmoke]::IsWindowVisible($handle) -and $timer.Elapsed.TotalSeconds -lt 15) {
        Start-Sleep -Milliseconds 200
    }
    if (-not [RuokTraySmoke]::IsWindowVisible($handle)) {
        throw 'Reopening RUOK did not restore the same window.'
    }
}
(Find-Name $text.Settings).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$selection = (Find-Id 'ReminderModeChoice').GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
if ($selection[0].Current.Name -ne $text.ReminderModeDisabled) {
    throw 'The test requires saved reminders to be Off; it will not alter an active user schedule.'
}
if ((Find-Id 'KeepInTrayChoice').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne 'On') {
    throw 'The test requires the Keep in tray option to be selected.'
}
try {
    Select-Mode $text.ReminderModeTimed
    if (-not (Find-Id 'NotificationStatus').Current.Name.Contains('Configured for every')) {
        throw 'The timed reminder setting was not applied.'
    }
    $app.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited -or [RuokTraySmoke]::IsWindowVisible($handle)) {
        throw 'Closing did not leave a hidden, live RUOK instance.'
    }
    Write-Output 'PASS: Close hides RUOK and keeps its reminder process alive.'
    Reopen-Ruok
    Write-Output 'PASS: Launching again restores the same RUOK window.'
}
finally {
    $app.Refresh()
    if ($app.HasExited) {
        throw 'RUOK exited before preferences could be restored. Reopen it and turn reminders Off.'
    }
    if (-not [RuokTraySmoke]::IsWindowVisible($handle)) { Reopen-Ruok }
    (Find-Name $text.Settings).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Select-Mode $text.ReminderModeDisabled
    if (-not (Find-Id 'NotificationStatus').Current.Name.Contains($text.ReminderOff)) {
        throw 'Reminders could not be restored to Off.'
    }
    Write-Output 'PASS: Original Off preference restored. No check-ins were saved.'
}
