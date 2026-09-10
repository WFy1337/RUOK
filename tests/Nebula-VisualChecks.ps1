# Loaded by Invoke-UiSmoke.ps1 -CheckNebula; uses its scoped UI Automation helpers.
Add-Type -AssemblyName System.Drawing
$drawingReferences = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Color].Assembly.Location,
    [System.Runtime.InteropServices.Marshal].Assembly.Location)
$drawingReferences += [System.Drawing.Bitmap].Assembly.GetReferencedAssemblies() |
    Where-Object Name -Like 'System.Private.Windows.*' |
    ForEach-Object { [System.Reflection.Assembly]::Load($_).Location }
Add-Type -ReferencedAssemblies ($drawingReferences | Select-Object -Unique) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public sealed class RuokNebulaFrame
{
    private readonly byte[] pixels;
    public int ColoredWidth { get; private set; }
    public int ColoredHeight { get; private set; }

    private RuokNebulaFrame(byte[] data, int width, int height)
    {
        pixels = data;
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            int b = data[p], g = data[p + 1], r = data[p + 2];
            bool colored = (b > 35 && b > r * 1.18 && b > g * 1.12)
                || (g > 45 && g > r * 1.30 && b > g * .85);
            if (!colored) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        ColoredWidth = Math.Max(0, right - left + 1);
        ColoredHeight = Math.Max(0, bottom - top + 1);
    }

    public double Difference(RuokNebulaFrame other)
    {
        if (pixels.Length != other.pixels.Length) throw new InvalidOperationException("The capture region changed.");
        long total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            for (int c = 0; c < 3; c++)
                total += Math.Abs(pixels[i + c] - other.pixels[i + c]);
        return total / (pixels.Length * .75);
    }

    public static RuokNebulaFrame Capture(IntPtr window, int process, int x, int y, int width, int height)
    {
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            RequireOwner(GetForegroundWindow(), process);
            Rect client;
            var origin = new Point();
            if (!GetClientRect(window, out client) || !ClientToScreen(window, ref origin))
                throw new InvalidOperationException("RUOK client bounds were unavailable.");
            if (width <= 0 || height <= 0 || x < origin.X || y < origin.Y
                || x + width > origin.X + client.Right || y + height > origin.Y + client.Bottom)
                throw new InvalidOperationException("The breathing capture must stay inside RUOK.");
            for (int row = 0; row < 9; row++)
            for (int col = 0; col < 9; col++)
                RequireOwner(WindowFromPoint(new Point(x + col * (width - 1) / 8, y + row * (height - 1) / 8)), process);
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
                var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var data = new byte[width * height * 4];
                try { Marshal.Copy(locked.Scan0, data, 0, data.Length); }
                finally { bitmap.UnlockBits(locked); }
                return new RuokNebulaFrame(data, width, height);
            }
        }
        finally { SetThreadDpiAwarenessContext(previousDpi); }
    }

    private static void RequireOwner(IntPtr window, int expected)
    {
        uint actual; GetWindowThreadProcessId(window, out actual);
        if (actual != expected) throw new InvalidOperationException("Keep RUOK foreground and unobscured during visual checks.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
}
'@

function Get-NebulaFrame {
    $phase = Find-ControlById 'PhaseText'
    $safety = Find-Control (Get-Text 'BreathingSafety') ([System.Windows.Automation.ControlType]::Text)
    if ($null -eq $phase -or $null -eq $safety) { throw 'Breathing landmarks were not found.' }
    $scale = [RuokNebulaFrame]::GetDpiForWindow($app.MainWindowHandle) / 96.0
    $center = $phase.Current.BoundingRectangle.Left + $phase.Current.BoundingRectangle.Width / 2
    $top = [Math]::Ceiling($safety.Current.BoundingRectangle.Bottom + 18 * $scale)
    $bottom = [Math]::Floor($phase.Current.BoundingRectangle.Top - 14 * $scale)
    $width = [int](340 * $scale)
    return [RuokNebulaFrame]::Capture($app.MainWindowHandle, $ProcessId,
        [int]($center - $width / 2), [int]$top, $width, [int]($bottom - $top))
}

function Assert-NebulaAmbient([string]$State) {
    Start-Sleep -Milliseconds 250
    $first = Get-NebulaFrame
    Start-Sleep -Milliseconds 2200
    $second = Get-NebulaFrame
    $difference = $first.Difference($second)
    if ($first.ColoredWidth -lt 70 -or $difference -lt .15) {
        throw "The $State nebula did not visibly animate (pixel difference $difference). Check Windows animation/high-contrast settings."
    }
    if ([Math]::Abs($first.ColoredWidth - $first.ColoredHeight) -gt $first.ColoredWidth * .10) {
        throw "The $State nebula appears clipped or distorted: $($first.ColoredWidth) x $($first.ColoredHeight)."
    }
    Write-Output ("PASS: {0} nebula visibly animates (mean channel difference {1:N3})." -f $State, $difference)
}

function Set-TestReducedMotion([bool]$Enabled) {
    Select-Page 'Settings' 'SettingsTitle'
    $motionControl = Find-ControlById 'ReducedMotionChoice'
    if ($null -eq $motionControl) { throw 'Reduced-motion preference was not found.' }
    $toggle = $motionControl.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq 'On') -ne $Enabled) {
        $toggle.Toggle()
        Start-Sleep -Milliseconds 650
        Wait-Check {
            $motionControl.Current.IsEnabled -and (($toggle.Current.ToggleState -eq 'On') -eq $Enabled)
        } 'Reduced-motion preference applied'
    }
    Select-Page 'Breathing' 'BreathingTitle'
    Start-Sleep -Milliseconds 400
}

function Assert-NebulaSuspension([string]$Description, [scriptblock]$Hide, [scriptblock]$Show) {
    $first = Get-NebulaFrame
    Start-Sleep -Milliseconds 2200
    $beforeHidden = Get-NebulaFrame
    $visibleDifference = $first.Difference($beforeHidden)
    try {
        & $Hide
        Start-Sleep -Milliseconds 3000
    }
    finally { & $Show }
    Start-Sleep -Milliseconds 200
    $afterHidden = Get-NebulaFrame
    $hiddenDifference = $beforeHidden.Difference($afterHidden)
    if ($visibleDifference -lt .15 -or $hiddenDifference -gt $visibleDifference * .60) {
        throw "$Description did not suspend the ambient timeline (visible delta $visibleDifference, hidden delta $hiddenDifference)."
    }
    Write-Output ("PASS: {0} suspends ambient motion and restores the same scene (visible/hidden delta {1:N3}/{2:N3})." -f $Description, $visibleDifference, $hiddenDifference)
}

function Invoke-NebulaVisualChecks {
    Select-Page 'Settings' 'SettingsTitle'
    $motionControl = Find-ControlById 'ReducedMotionChoice'
    if ($null -eq $motionControl) { throw 'Reduced-motion preference was not found.' }
    $originalReduced = $motionControl.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On'
    $script:nebulaCompletionMotionEnabled = -not $originalReduced
    try {
        Set-TestReducedMotion $false
        Assert-NebulaAmbient 'Ready'
        $scrollControl = Find-ControlById 'BreathingScroll'
        if ($null -eq $scrollControl) { throw 'Breathing viewport was not found.' }
        $scroll = $scrollControl.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) {
            $originalScroll = $scroll.Current.VerticalScrollPercent
            $originalBounds = $root.Current.BoundingRectangle
            $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
            Assert-NebulaSuspension 'Scrolling the orb out of view' {
                # At full height, a sliver of the orb control can remain visible even at maximum scroll.
                $transform.Resize($originalBounds.Width, 600)
                Start-Sleep -Milliseconds 150
                $scroll.SetScrollPercent(-1, 100)
            } {
                $scroll.SetScrollPercent(-1, $originalScroll)
                $transform.Resize($originalBounds.Width, $originalBounds.Height)
            }
        }
        else { Write-Output 'NOT RUN: Viewport suspension requires a window short enough to scroll the orb offscreen.' }
        $windowPattern = $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $originalWindowState = $windowPattern.Current.WindowVisualState
        Assert-NebulaSuspension 'Minimizing RUOK' {
            $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
        } {
            $windowPattern.SetWindowVisualState($originalWindowState)
        }
        Invoke-Button 'Start'
        Wait-Check { Test-Phase 'BreathExhale' } 'Inhale reaches maximum expansion' 25
        $expanded = Get-NebulaFrame
        Wait-Check { Test-Phase 'BreathInhale' } 'Exhale returns to minimum expansion' 25
        $contracted = Get-NebulaFrame
        if ($expanded.ColoredWidth -lt $contracted.ColoredWidth * 1.15) {
            throw "The orb did not visibly expand/contract: $($expanded.ColoredWidth) / $($contracted.ColoredWidth) pixels."
        }
        Write-Output "PASS: Breathing changes the visible diameter ($($contracted.ColoredWidth) to $($expanded.ColoredWidth) px)."
        Invoke-Button 'Pause'
        Wait-Check { Test-Phase 'BreathPaused' } 'Breathing pauses for independent ambient-motion check'
        Start-Sleep -Milliseconds 200
        $paused = Get-NebulaFrame
        Assert-NebulaAmbient 'Paused'
        $stillPaused = Get-NebulaFrame
        if (-not (Test-Phase 'BreathPaused') -or [Math]::Abs($paused.ColoredWidth - $stillPaused.ColoredWidth) -gt 8) {
            throw 'Pausing did not preserve the breathing size/phase while the nebula moved.'
        }
        Write-Output 'PASS: Pause freezes expansion without stopping the nebula.'
        Invoke-Button 'Stop'
        Assert-NebulaAmbient 'Stopped'
        Set-TestReducedMotion $true
        $first = Get-NebulaFrame
        Start-Sleep -Milliseconds 2200
        $second = Get-NebulaFrame
        if ($first.ColoredWidth -lt 70 -or $first.Difference($second) -gt .02) {
            throw 'Reduced motion did not retain a fully static, visible nebula.'
        }
        Invoke-Button 'Start'
        Wait-Check { Test-Phase 'BreathExhale' } 'Static mode keeps the breathing timeline running' 25
        $runningStatic = Get-NebulaFrame
        if ($first.Difference($runningStatic) -gt .02) { throw 'Static mode changed the nebula during breathing.' }
        Invoke-Button 'Stop'
        Write-Output 'PASS: Reduced motion stops both ambient and breathing-size animation without stopping the timeline.'
        Set-TestReducedMotion $false
        for ($index = 0; $index -lt 5; $index++) {
            Select-Page 'Dashboard' 'DashboardTitle'
            Select-Page 'Breathing' 'BreathingTitle'
            Start-Sleep -Milliseconds 200
        }
        Assert-NebulaAmbient 'Reloaded'
    }
    finally {
        $app.Refresh()
        if ($app.HasExited) { throw 'RUOK exited before its original reduced-motion preference could be restored.' }
        Select-Page 'Breathing' 'BreathingTitle'
        if (-not (Test-Phase 'BreathReady')) {
            Invoke-Button 'Stop'
        }
        Set-TestReducedMotion $originalReduced
    }
    Write-Output 'Nebula checks kept frames in memory and restored the original motion preference. No wellbeing records or reminder settings were changed.'
}
