# Loaded by the existing native UI smoke runner; never targets RUOK Testing.
function Show-EncouragementControl([string]$Id) {
    $target = Find-ControlById $Id
    if ($null -eq $target) { throw "Missing encouragement control: $Id" }
    $target.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
    Start-Sleep -Milliseconds 100
    $scroll = (Find-ControlById 'SettingsScroll').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    for ($i = 0; $i -lt 40; $i++) {
        $control = Find-ControlById $Id
        if ($null -eq $control) { throw "Missing encouragement control: $Id" }
        $viewport = (Find-ControlById 'SettingsScroll').Current.BoundingRectangle
        $window = $root.Current.BoundingRectangle
        $rect = $control.Current.BoundingRectangle
        $top = [Math]::Max($viewport.Top, $window.Top)
        $bottom = [Math]::Min($viewport.Bottom, $window.Bottom)
        if (-not $control.Current.IsOffscreen -and -not $rect.IsEmpty -and $rect.Top -ge $top -and $rect.Bottom -le $bottom) {
            return $control
        }
        $direction = if (-not $rect.IsEmpty -and $rect.Top -lt $top) { 'SmallDecrement' } else { 'SmallIncrement' }
        $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
            [System.Windows.Automation.ScrollAmount]::$direction)
        Start-Sleep -Milliseconds 50
    }
    throw "Could not reveal encouragement control: $Id"
}

function Get-EncouragementValuePattern([string]$Id) {
    $control = Show-EncouragementControl $Id
    $pattern = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        return $pattern
    }
    if ($control.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) {
        return $pattern
    }
    $editor = $control.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit))
    if ($null -eq $editor) { throw "Missing value editor: $Id" }
    return $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
}

function Set-EncouragementText([string]$Id, [string]$Value) {
    (Get-EncouragementValuePattern $Id).SetValue($Value)
    (Find-ControlById 'EncouragementEnabled').SetFocus()
}

function Set-EncouragementEnabled([bool]$Enabled) {
    $toggle = (Show-EncouragementControl 'EncouragementEnabled').GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)
    $on = $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    if ($on -ne $Enabled) { $toggle.Toggle() }
}

function Invoke-EncouragementButton([string]$Id) {
    $button = Show-EncouragementControl $Id
    if (-not $button.Current.IsEnabled) { throw "Disabled encouragement button: $Id" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
}

function Save-EncouragementSettings([bool]$Enabled) {
    Invoke-EncouragementButton 'SaveEncouragements'
    $expected = if ($Enabled) { (Get-Text 'EncouragementScheduleHint') -f 15, 30 } else { Get-Text 'EncouragementOff' }
    Wait-Check {
        $status = Find-ControlById 'EncouragementStatus'
        $refresh = Find-ControlById 'RefreshLocalData'
        $saved = Find-Control (Get-Text 'EncouragementSaved') ([System.Windows.Automation.ControlType]::Text)
        $null -ne $status -and $status.Current.Name -eq $expected -and
            $null -ne $saved -and $null -ne $refresh -and $refresh.Current.IsEnabled
    } 'Encouragement settings saved without changing PulseCheck'
}

function Reopen-EncouragementWindow([IntPtr]$Handle) {
    if ($StandaloneExecutable) {
        Start-Process -FilePath $StandaloneExecutable
    } else {
        Start-Process -FilePath explorer.exe -ArgumentList 'shell:AppsFolder\F8FF2497-B789-464A-9326-7774176A2B0F_1z32rh13vfry6!App'
    }
    Wait-Check { [RuokEncouragementSmoke]::IsWindowVisible($Handle) } 'Stable RUOK reopens from its tray'
    $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 200
}

function Invoke-EncouragementChecks {
    if (-not $StandaloneExecutable) {
        $package = Get-AppxPackage -Name 'F8FF2497-B789-464A-9326-7774176A2B0F'
        if ($null -eq $package -or $package.PackageFamilyName -ne 'F8FF2497-B789-464A-9326-7774176A2B0F_1z32rh13vfry6' -or
            -not $app.Path.StartsWith($package.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Encouragement checks require the registered stable RUOK package, not RUOK Testing.'
        }
    }
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class RuokEncouragementSmoke {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
    $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 200
    Select-Page 'Settings' 'SettingsTitle'
    if ($StandaloneExecutable) {
        $location = Find-Control (Get-Text 'DataLocation') ([System.Windows.Automation.ControlType]::Edit)
        $expected = Join-Path $env:LOCALAPPDATA 'RUOK\Standalone\Data\ruok.db'
        if ($null -eq $location -or
            $location.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $expected) {
            throw 'Standalone checks require the separate standalone data profile.'
        }
    }
    Wait-Check {
        $button = Find-ControlById 'SaveEncouragements'
        $null -ne $button -and $button.Current.IsEnabled
    } 'Motivational notification controls are ready'
    $mode = (Find-ControlById 'ReminderModeChoice').GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    $enabled = (Find-ControlById 'EncouragementEnabled').GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    if ($mode.Count -ne 1 -or $enabled -ne 'Off') {
        throw 'These checks require saved encouragements Off; an active encouragement schedule will not be changed.'
    }
    $originalMode = $mode[0].Current.Name
    $encouragementOnly = $originalMode -eq (Get-Text 'ReminderModeDisabled')
    if ((Find-ControlById 'KeepInTrayChoice').GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne 'On') {
        throw 'These checks require the existing Keep in tray preference selected.'
    }
    $originalMessages = (Get-EncouragementValuePattern 'EncouragementMessages').Current.Value
    $originalInterval = (Get-EncouragementValuePattern 'EncouragementInterval').Current.Value
    $originalBounds = $root.Current.BoundingRectangle
    $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $handle = $app.MainWindowHandle
    $sample = "Small steps count.`r`nOne moment at a time."
    try {
        foreach ($width in @(800, 1180)) {
            $transform.Resize($width, 860)
            Start-Sleep -Milliseconds 200
            foreach ($id in @('EncouragementInterval', 'EncouragementMessages', 'SaveEncouragements',
                    'PreviewEncouragement', 'RestoreEncouragementMessages')) {
                Show-EncouragementControl $id | Out-Null
                Assert-VisibleControl $id
            }
            Write-Output "PASS: Encouragement controls stay contained at width $width."
        }
        Set-EncouragementText 'EncouragementMessages' $sample
        Set-EncouragementText 'EncouragementInterval' '15'
        Save-EncouragementSettings $false
        Invoke-Button 'Refresh'
        Wait-Check {
            (Find-ControlById 'SaveEncouragements').Current.IsEnabled
        } 'Refresh reloads saved encouragement settings'
        $reloaded = (Get-EncouragementValuePattern 'EncouragementMessages').Current.Value.Replace("`r`n", "`n").Replace("`r", "`n")
        if ($reloaded -ne $sample.Replace("`r`n", "`n") -or
            (Get-EncouragementValuePattern 'EncouragementInterval').Current.Value -ne '15') {
            throw 'Custom messages or spacing did not persist.'
        }
        Invoke-EncouragementButton 'PreviewEncouragement'
        Wait-Check {
            $null -ne (Find-Control (Get-Text 'EncouragementPreviewSent') ([System.Windows.Automation.ControlType]::Text))
        } 'Windows accepts the safe encouragement preview while automatic delivery is Off'
        if ((Find-ControlById 'EncouragementStatus').Current.Name -ne (Get-Text 'EncouragementOff')) {
            throw 'Preview unexpectedly enabled automatic encouragements.'
        }
        Set-EncouragementEnabled $true
        Save-EncouragementSettings $true
        $app.CloseMainWindow() | Out-Null
        $trayDescription = if ($encouragementOnly) { 'Encouragement-only Close stays in the tray' } else { 'Close stays in the tray without changing the active PulseCheck schedule' }
        Wait-Check { -not [RuokEncouragementSmoke]::IsWindowVisible($handle) } $trayDescription
        Reopen-EncouragementWindow $handle
        if (-not $encouragementOnly) {
            Write-Output 'NOTE: Encouragement-only tray isolation was not asserted because the existing PulseCheck schedule was preserved.'
        }
        Select-Page 'Settings' 'SettingsTitle'
        Set-EncouragementEnabled $false
        Save-EncouragementSettings $false
        Invoke-EncouragementButton 'RestoreEncouragementMessages'
        Wait-Check {
            $null -ne (Find-Control (Get-Text 'EncouragementDefaultsLoaded') ([System.Windows.Automation.ControlType]::Text))
        } 'Default message restoration remains an explicit editor action'
        $defaults = (Get-EncouragementValuePattern 'EncouragementMessages').Current.Value
        if (-not $defaults.Contains('Keep it going!')) { throw 'Original encouragement messages were not restored.' }
    }
    finally {
        $app.Refresh()
        if ($app.HasExited) { throw 'RUOK exited before settings could be restored. Reopen stable RUOK and review encouragement settings.' }
        if (-not [RuokEncouragementSmoke]::IsWindowVisible($handle)) { Reopen-EncouragementWindow $handle }
        Select-Page 'Settings' 'SettingsTitle'
        Set-EncouragementEnabled $false
        Set-EncouragementText 'EncouragementMessages' $originalMessages
        Set-EncouragementText 'EncouragementInterval' $originalInterval
        Save-EncouragementSettings $false
        $transform.Resize($originalBounds.Width, $originalBounds.Height)
        Show-EncouragementControl 'EncouragementEnabled' | Out-Null
    }
    $afterMode = (Find-ControlById 'ReminderModeChoice').GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($afterMode[0].Current.Name -ne $originalMode) { throw 'The PulseCheck schedule changed.' }
    Write-Output 'PASS: Original encouragement settings restored; no check-ins, consent, retention or PulseCheck settings changed.'
}
