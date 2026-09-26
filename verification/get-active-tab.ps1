# Reads the currently-selected Windows Terminal tab title via UI Automation.
# Used to verify TabCycler actually switches tabs (Ctrl+Tab), independent of
# anything TabCycler reports about itself.
param([switch]$All)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$wt = Get-Process -Name WindowsTerminal -ErrorAction SilentlyContinue |
      Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $wt) { Write-Output 'NO-WT'; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($wt.MainWindowHandle)
if (-not $root) { Write-Output 'NO-ROOT'; exit 1 }

$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TabItem)

$tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

$rows = foreach ($t in $tabs) {
    $isSel = $false
    try {
        $pat = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSel = $pat.Current.IsSelected
    } catch { $isSel = $false }
    [pscustomobject]@{ Title = $t.Current.Name; Selected = $isSel }
}

if ($All) { $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Output }
($rows | Where-Object Selected | Select-Object -First 1).Title
