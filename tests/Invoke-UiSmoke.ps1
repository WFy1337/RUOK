#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,
    [string]$StandaloneExecutable,
    [switch]$CheckCompletion,
    [switch]$CheckCompactLayout,
    [switch]$CheckFaceSelection,
    [switch]$CheckNebula,
    [switch]$CheckEncouragements,
    [switch]$EncouragementsOnly,
    [switch]$AlignmentOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$app = Get-Process -Id $ProcessId
if ($StandaloneExecutable) {
    $StandaloneExecutable = (Resolve-Path -LiteralPath $StandaloneExecutable).ProviderPath
    if (-not [string]::Equals($app.Path, $StandaloneExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The standalone test process must match the explicitly supplied executable path.'
    }
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class RuokStandaloneIdentity {
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GetPackageFullName(IntPtr process, ref uint length, IntPtr name);
}
'@
    [uint32]$packageLength = 0
    if ([RuokStandaloneIdentity]::GetPackageFullName($app.Handle, [ref]$packageLength, [IntPtr]::Zero) -ne 15700) {
        throw 'Standalone checks cannot target a packaged app or RUOK Testing.'
    }
} elseif ($app.ProcessName -ne 'RUOK.App') {
    throw 'The supplied process must be a running RUOK.App with a native window.'
}
if ($app.MainWindowHandle -eq 0 -or $app.MainWindowTitle -ne 'RUOK') {
    throw 'The supplied process must expose the native RUOK window.'
}
$root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
$scope = [System.Windows.Automation.TreeScope]::Descendants
$resources = @{}
[xml]$document = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\RUOK.App\Resources\UiStrings.resx') -Raw -Encoding UTF8
foreach ($entry in $document.root.data) {
    $resources[[string]$entry.name] = [string]$entry.value
}

function Get-Text([string]$Key) {
    if (-not $resources.ContainsKey($Key)) {
        throw "Missing test resource: $Key"
    }
    return $resources[$Key]
}

function Find-Control([string]$Name, [System.Windows.Automation.ControlType]$Type) {
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $typeCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type)
    return $root.FindFirst($scope,
        [System.Windows.Automation.AndCondition]::new($nameCondition, $typeCondition))
}

function Find-ControlById([string]$AutomationId) {
    return $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId))
}

function Wait-Check([scriptblock]$Condition, [string]$Description, [int]$TimeoutSeconds = 8) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $app.Refresh()
        if ($app.HasExited -or -not $app.Responding) {
            throw "RUOK stopped responding: $Description"
        }
        if (& $Condition) {
            Write-Output "PASS: $Description"
            return
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "Timed out: $Description"
}

function Select-Page([string]$NavigationKey, [string]$HeadingKey) {
    $item = Find-Control (Get-Text $NavigationKey) ([System.Windows.Automation.ControlType]::ListItem)
    if ($null -eq $item) {
        $toggle = Find-ControlById 'TogglePaneButton'
        if ($null -eq $toggle) {
            throw "Missing navigation item: $NavigationKey"
        }
        $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Check {
            $null -ne (Find-Control (Get-Text $NavigationKey) ([System.Windows.Automation.ControlType]::ListItem))
        } 'Compact navigation opens'
        $item = Find-Control (Get-Text $NavigationKey) ([System.Windows.Automation.ControlType]::ListItem)
    }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Check {
        $null -ne (Find-Control (Get-Text $HeadingKey) ([System.Windows.Automation.ControlType]::Text))
    } "$NavigationKey page renders"
}

function Invoke-Button([string]$ResourceKey) {
    $button = Find-Control (Get-Text $ResourceKey) ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $button -or -not $button.Current.IsEnabled) {
        throw "Missing or disabled button: $ResourceKey"
    }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Test-Phase([string]$ResourceKey) {
    $phase = Find-ControlById 'PhaseText'
    return $null -ne $phase -and $phase.Current.Name -eq (Get-Text $ResourceKey)
}

function Assert-VisibleControl([string]$AutomationId) {
    $control = Find-ControlById $AutomationId
    if ($null -eq $control -or $control.Current.IsOffscreen) {
        throw "A primary action is not visible: $AutomationId"
    }
    $bounds = $root.Current.BoundingRectangle
    $rect = $control.Current.BoundingRectangle
    if ($rect.Left -lt $bounds.Left -or $rect.Right -gt $bounds.Right) {
        throw "A primary action overflows horizontally: $AutomationId"
    }
}

function Test-PaneOpen {
    $label = Find-Control (Get-Text 'Refresh') ([System.Windows.Automation.ControlType]::Text)
    return $null -ne $label -and -not $label.Current.IsOffscreen
}

function Assert-Alignment([string]$Description) {
    $group = Find-ControlById 'MoodChoices'
    $radios = $root.FindAll($scope, $radioCondition)
    if ($null -eq $group -or $radios.Count -ne 5) {
        throw "Missing mood layout: $Description"
    }
    $groupBounds = $group.Current.BoundingRectangle
    $rects = @($radios | ForEach-Object { $_.Current.BoundingRectangle })
    $left = ($rects | Measure-Object Left -Minimum).Minimum
    $right = ($rects | Measure-Object Right -Maximum).Maximum
    $widths = $rects | Measure-Object Width -Minimum -Maximum
    $centerError = [Math]::Abs(($left + $right) / 2 - ($groupBounds.Left + $groupBounds.Width / 2))
    if ($centerError -gt 2 -or $left -lt $groupBounds.Left - 1 -or $right -gt $groupBounds.Right + 1) {
        throw "Mood tiles are not centered/contained: $Description (center error $centerError px)."
    }
    if ($widths.Maximum - $widths.Minimum -gt 1.5 -or ($right - $left) / $groupBounds.Width -lt 0.97) {
        throw "Mood tiles do not share the available width evenly: $Description."
    }
    $refresh = Find-ControlById 'RefreshLocalData'
    $toggle = Find-ControlById 'TogglePaneButton'
    if ($null -ne $refresh -and -not $refresh.Current.IsOffscreen -and -not (Test-PaneOpen)) {
        $buttonBounds = $refresh.Current.BoundingRectangle
        $toggleBounds = $toggle.Current.BoundingRectangle
        $offset = [Math]::Abs(($buttonBounds.Left + $buttonBounds.Width / 2) - ($toggleBounds.Left + $toggleBounds.Width / 2))
        if ($offset -gt 2) {
            throw "Compact Refresh is not centered on the navigation rail: $Description ($offset px)."
        }
    }
    Write-Output "PASS: Centered, equal-width mood tiles and aligned compact Refresh ($Description)."
}

if ($CheckEncouragements -or $EncouragementsOnly) {
    . (Join-Path $PSScriptRoot 'Encouragement-VisualChecks.ps1')
    Invoke-EncouragementChecks
    if ($EncouragementsOnly) { return }
}

Select-Page 'Dashboard' 'DashboardTitle'
Select-Page 'PulseCheck' 'PulseTitle'
$radioCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
Wait-Check { $root.FindAll($scope, $radioCondition).Count -eq 5 } 'Five mood choices are available'
$moods = $root.FindAll($scope, $radioCondition)
for ($index = 0; $index -lt 5; $index++) {
    $score = $index + 1
    $expected = (Get-Text 'MoodAccessible') -f (Get-Text "Mood$score"), $score
    if ($moods[$index].Current.Name -ne $expected) {
        throw "Mood $score does not expose its text and numeric value as an accessible name."
    }
}
Write-Output 'PASS: All five native radio buttons expose meaningful accessible names.'

if ($CheckFaceSelection) {
    foreach ($mood in $moods) {
        if ($mood.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
            throw 'Finish or skip the current draft before testing face selection.'
        }
    }
    foreach ($mood in $moods) {
        $mood.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $selected = @($moods | Where-Object {
            $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        })
        if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne $mood.Current.Name) {
            throw 'Selecting a face did not produce exactly one matching radio selection.'
        }
    }
    Invoke-Button 'Skip'
    foreach ($mood in $moods) {
        if ($mood.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
            throw 'Skip did not clear the face preview.'
        }
    }
    Write-Output 'PASS: Each full-face radio is selectable; exactly one is selected, and Skip clears the preview without saving.'
}

if ($null -ne (Find-ControlById 'AcceptPrivacy')) {
    $save = Find-ControlById 'SavePulse'
    if ($null -eq $save -or $save.Current.IsEnabled) {
        throw 'Saving must be disabled while privacy acknowledgement is required.'
    }
    Write-Output 'PASS: Saving is disabled while the privacy acknowledgement is visible.'
}
else {
    Write-Output 'NOT RUN: First-use privacy gate is no longer visible; consent is not changed by this test.'
}

if ($AlignmentOnly) {
    $originalBounds = $root.Current.BoundingRectangle
    $originalPaneOpen = Test-PaneOpen
    $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    try {
        foreach ($width in @(900, 1200, 1600)) {
            $transform.Resize($width, 900)
            Start-Sleep -Milliseconds 400
            Assert-Alignment "$width px, initial pane state"
            (Find-ControlById 'TogglePaneButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 400
            Assert-Alignment "$width px, toggled pane"
            (Find-ControlById 'TogglePaneButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 400
            Assert-Alignment "$width px, restored pane"
        }
    }
    finally {
        $transform.Resize($originalBounds.Width, $originalBounds.Height)
        Start-Sleep -Milliseconds 400
        if ((Test-PaneOpen) -ne $originalPaneOpen) {
            (Find-ControlById 'TogglePaneButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
    }
    Write-Output 'Native alignment checks passed. No data, preferences, or draft selections were changed.'
    return
}

Select-Page 'History' 'HistoryTitle'
Select-Page 'Settings' 'SettingsTitle'
Select-Page 'About' 'AboutTitle'
Select-Page 'Breathing' 'BreathingTitle'
foreach ($id in @('StartButton', 'PauseButton', 'RestartButton', 'StopButton')) {
    Assert-VisibleControl $id
}
if (-not (Test-Phase 'BreathReady')) {
    throw 'Finish or stop the current breathing exercise before running this smoke test.'
}

Invoke-Button 'Start'
Wait-Check { Test-Phase 'BreathInhale' } 'Breathing starts with an inhale'
Invoke-Button 'Pause'
Wait-Check { Test-Phase 'BreathPaused' } 'Pause displays the paused state'
Invoke-Button 'Resume'
Wait-Check { (Test-Phase 'BreathInhale') -or (Test-Phase 'BreathExhale') } 'Resume restores a breathing phase'
Invoke-Button 'Restart'
Wait-Check { Test-Phase 'BreathInhale' } 'Restart returns to the inhale phase'
Select-Page 'Dashboard' 'DashboardTitle'
Select-Page 'Breathing' 'BreathingTitle'
Wait-Check { Test-Phase 'BreathPaused' } 'Navigating away pauses the exercise'
Invoke-Button 'Stop'
Wait-Check { Test-Phase 'BreathReady' } 'Stop resets the exercise'

if ($CheckNebula) {
    . (Join-Path $PSScriptRoot 'Nebula-VisualChecks.ps1')
    Invoke-NebulaVisualChecks
}

if ($CheckCompletion) {
    $duration = Find-ControlById 'BreathingDuration'
    $selection = $duration.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -ne 1 -or $selection[0].Current.Name -ne ((Get-Text 'Minutes') -f 1)) {
        throw 'Select a one-minute exercise before using -CheckCompletion.'
    }
    Invoke-Button 'Start'
    Wait-Check { Test-Phase 'BreathComplete' } 'A full minute reaches the completed state (keep RUOK active)' 75
    if ($CheckNebula -and $nebulaCompletionMotionEnabled) {
        Assert-NebulaAmbient 'Completed'
    }
    $pause = Find-Control (Get-Text 'Pause') ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $pause -or $pause.Current.IsEnabled) {
        throw 'Pause must be disabled after the exercise completes.'
    }
    Select-Page 'Dashboard' 'DashboardTitle'
    Select-Page 'Breathing' 'BreathingTitle'
    Wait-Check { Test-Phase 'BreathComplete' } 'Navigating after completion does not attempt to pause a completed session'
    Invoke-Button 'Restart'
    Wait-Check { Test-Phase 'BreathInhale' } 'A completed exercise can restart'
    Invoke-Button 'Stop'
    Wait-Check { Test-Phase 'BreathReady' } 'Stop resets the restarted exercise'
}

if ($CheckCompactLayout) {
    $originalBounds = $root.Current.BoundingRectangle
    $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    try {
        $transform.Resize(800, 620)
        Start-Sleep -Milliseconds 200
        Select-Page 'PulseCheck' 'PulseTitle'
        Assert-VisibleControl 'SavePulse'
        $bounds = $root.Current.BoundingRectangle
        $moods = $root.FindAll($scope, $radioCondition)
        if ($moods.Count -ne 5) {
            throw 'The compact layout must retain all five mood choices.'
        }
        foreach ($mood in $moods) {
            $rect = $mood.Current.BoundingRectangle
            if ($rect.Left -lt $bounds.Left -or $rect.Right -gt $bounds.Right) {
                throw 'A mood choice overflows the compact window horizontally.'
            }
        }
        Select-Page 'Breathing' 'BreathingTitle'
        foreach ($id in @('StartButton', 'PauseButton', 'RestartButton', 'StopButton')) {
            Assert-VisibleControl $id
        }
        Write-Output 'PASS: Compact mood layout and pinned primary actions remain usable.'
    }
    finally {
        $transform.Resize($originalBounds.Width, $originalBounds.Height)
    }
}

Select-Page 'Dashboard' 'DashboardTitle'

if ($CheckNebula) {
    Write-Output 'Native UI smoke checks passed. No entries, consent, exports, retention, or reminder settings were changed; the original motion preference was restored.'
}
else {
    Write-Output 'Native UI smoke checks passed. No entries, consent, settings, exports, or retention were changed.'
}
