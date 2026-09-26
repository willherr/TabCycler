# Read-only observer: samples what the TabCycler widget is DISPLAYING and
# appends it to a file. It never clicks, never moves the cursor, never changes
# focus, and never sends input. Safe to leave running while the user tests.
param(
    [int]$Samples = 240,
    [int]$IntervalMs = 500,
    [string]$OutFile = "$env:LOCALAPPDATA\TabCycler\watch.txt"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$lines = [System.Collections.Generic.List[string]]::new()
$prev = ''

for ($i = 1; $i -le $Samples; $i++) {
    $p = Get-Process -Name TabCycler -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) {
        Add-Content -LiteralPath $OutFile -Value "$(Get-Date -Format HH:mm:ss.fff)  WIDGET NOT RUNNING"
        Start-Sleep -Milliseconds $IntervalMs
        continue
    }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $names = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) |
               ForEach-Object { $_.Current.Name })
    # Order is: title, status, detail, button, X
    $status = if ($names.Count -gt 1) { $names[1] } else { '?' }
    $detail = if ($names.Count -gt 2) { $names[2] } else { '?' }
    $button = if ($names.Count -gt 3) { $names[3] } else { '?' }
    $row = '[' + $button + '] ' + $status + ' | ' + $detail

    if ($row -ne $prev) {
        Add-Content -LiteralPath $OutFile -Value ("$(Get-Date -Format HH:mm:ss.fff)  $row")
        $prev = $row
    }
    Start-Sleep -Milliseconds $IntervalMs
}
