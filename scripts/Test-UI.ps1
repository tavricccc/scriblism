param([string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$rootPath = Split-Path $PSScriptRoot -Parent
if (!$Executable) { $Executable = Join-Path $rootPath 'src/Scriblism.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/Scriblism.exe' }
$testRoot = Join-Path $rootPath ('artifacts/ui-test-' + [Guid]::NewGuid().ToString('N'))
$workspace = Join-Path $testRoot 'workspace'
New-Item -ItemType Directory -Force $workspace | Out-Null
$file = Join-Path $workspace 'main.txt'
[IO.File]::WriteAllText($file, "alpha`nbeta`n")
[IO.File]::WriteAllText((Join-Path $workspace 'other.py'), "def hello():`n    return 'world'`n")
New-Item -ItemType Directory (Join-Path $workspace 'child') | Out-Null
$oldState = $env:SCRIBLISM_STATE_ROOT
$env:SCRIBLISM_STATE_ROOT = Join-Path $testRoot 'profile'
$checks = [Collections.Generic.List[object]]::new()
$script:process = $null
$script:window = $null
function Start-App([string[]]$Paths) {
    $arguments = @($Paths | ForEach-Object { '"' + $_ + '"' })
    $start = @{ FilePath=$Executable; PassThru=$true }
    if ($arguments.Count -gt 0) { $start.ArgumentList=$arguments }
    $script:process = Start-Process @start
    for ($i=0;$i -lt 100;$i++) {
        Start-Sleep -Milliseconds 100; $script:process.Refresh()
        if ($script:process.HasExited) { throw 'The application exited during startup.' }
        if ($script:process.MainWindowHandle -ne 0) { break }
    }
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
    Start-Sleep -Milliseconds 700
}
function Find-Element([string]$Value, [switch]$ById, [Windows.Automation.ControlType]$Type) {
    $property = if ($ById) { [Windows.Automation.AutomationElement]::AutomationIdProperty } else { [Windows.Automation.AutomationElement]::NameProperty }
    for ($i=0;$i -lt 50;$i++) {
        $elements = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new($property,$Value))
        foreach ($element in $elements) {
            if (!$element.Current.IsOffscreen -and $element.Current.IsEnabled -and (!$Type -or $element.Current.ControlType -eq $Type)) { return $element }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "UI element not found: $Value"
}
function Invoke-Element([string]$Name) {
    (Find-Element $Name).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}
function Invoke-Menu([string]$Menu,[string]$Item) {
    (Find-Element $Menu -Type ([Windows.Automation.ControlType]::MenuItem)).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 150
    (Find-Element $Item -Type ([Windows.Automation.ControlType]::MenuItem)).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}
function Set-Text([string]$Id,[string]$Text) {
    (Find-Element $Id -ById).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
    Start-Sleep -Milliseconds 200
}
function Select-Tab([string]$Name) {
    (Find-Element $Name -Type ([Windows.Automation.ControlType]::TabItem)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
}
function Replace-Text([string]$Query,[string]$Replacement,[bool]$Regex=$false) {
    Invoke-Menu '編輯' '取代'
    $toggle=(Find-Element 'RegexOption' -ById).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $Regex) { $toggle.Toggle() }
    Set-Text 'FindBox' $Query; Set-Text 'ReplaceBox' $Replacement
    Invoke-Element '全部取代'; Invoke-Element '關閉搜尋'
}
function Check([string]$Name,[bool]$Passed,[string]$Detail='') {
    $checks.Add(@{ name=$Name; passed=$Passed; detail=$Detail })
    if (!$Passed) { throw "Check failed: $Name ($Detail)" }
}
function Editor-Text {
    return (Find-Element 'DocumentEditor' -ById).GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1)
}
try {
    Start-App @($workspace,(Join-Path $workspace 'other.py'),$file)
    for ($i=0;$i -lt 50;$i++) {
        if ((Find-Element 'PathStatus' -ById).Current.Name.EndsWith('main.txt')) { break }
        Start-Sleep -Milliseconds 100
    }
    $tree = Find-Element 'FileTree' -ById
    $items = $tree.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text))
    $names = @($items | ForEach-Object { $_.Current.Name })
    Check 'tree shows filenames rather than CLR type names' ($names -contains 'main.txt' -and $names -contains 'other.py') ($names -join ', ')
    $before = (Find-Element 'DocumentEditor' -ById).Current.BoundingRectangle
    Replace-Text 'beta' 'betagamma'; Invoke-Menu '檔案' '儲存'
    Check 'replace and save reach disk' ([IO.File]::ReadAllText($file) -eq "alpha`nbetagamma`n") ([IO.File]::ReadAllText($file))
    $after = (Find-Element 'DocumentEditor' -ById).Current.BoundingRectangle
    Check 'toast and find preserve editor bounds' ($before -eq $after) "$before -> $after"
    Invoke-Menu '編輯' '復原'
    Check 'undo reverses text rather than formatting' (!(Editor-Text).Contains('gamma')) (Editor-Text)
    Invoke-Menu '編輯' '重做'
    Check 'redo restores text' ((Editor-Text).Contains('gamma')) (Editor-Text)
    Select-Tab 'other.py'
    Check 'tab selection changes document' ((Find-Element 'PathStatus' -ById).Current.Name.EndsWith('other.py'))
    Select-Tab 'main.txt'
    Check 'returning to tab preserves document' ((Editor-Text).Contains('gamma')) (Editor-Text)
    Replace-Text 'beta' 'delta'
    [IO.File]::WriteAllText($file,'external edit')
    Invoke-Menu '檔案' '儲存'
    $null=Find-Element '確認覆寫檔案'; Invoke-Element '取消'
    Check 'canceling external conflict preserves disk file' ([IO.File]::ReadAllText($file) -eq 'external edit')
    Invoke-Menu '檔案' '關閉分頁'; Invoke-Element '取消'
    Check 'canceling close preserves dirty document' ((Editor-Text).Contains('deltagamma')) (Editor-Text)
    Invoke-Menu '檔案' '關閉分頁'; Invoke-Element '不儲存'
    Check 'discard closes only requested tab' (!(Find-Element 'PathStatus' -ById).Current.Name.EndsWith('main.txt'))
    Invoke-Menu '檔案' '新增文件'; Replace-Text '^' 'recovery-only-text' $true
    Check 'unsaved recovery fixture entered' ((Editor-Text).Contains('recovery-only-text')) (Editor-Text)
    $recovery = @()
    for ($i=0;$i -lt 60;$i++) {
        Start-Sleep -Milliseconds 100
        $recovery = @(Get-ChildItem (Join-Path $env:SCRIBLISM_STATE_ROOT 'Recovery') -Filter '*.json')
        if ($recovery.Count -gt 0) { break }
    }
    Check 'dirty document has on-disk recovery' ($recovery.Count -gt 0)
    $script:process.Kill(); $null=$script:process.WaitForExit(10000)
    Start-App @(); Invoke-Element '復原'
    Check 'recovery restores text after killed test process' ((Editor-Text).Contains('recovery-only-text')) (Editor-Text)
    Invoke-Menu '檔案' '關閉分頁'; Invoke-Element '不儲存'
    $null=$script:process.CloseMainWindow()
    Check 'clean shutdown succeeds' ($script:process.WaitForExit(10000))
} catch {
    $checks.Add(@{name='exception';passed=$false;detail=$_.ToString()})
} finally {
    $env:SCRIBLISM_STATE_ROOT=$oldState
    if ($script:process -and !$script:process.HasExited) {
        # Only this test's process is terminated. Its isolated recovery files remain for diagnosis.
        $script:process.Kill(); $null=$script:process.WaitForExit(10000)
    }
    $passed = @($checks | Where-Object { !$_.passed }).Count -eq 0
    $output = Join-Path $rootPath 'artifacts/test-results/ui.json'
    New-Item -ItemType Directory -Force (Split-Path $output -Parent) | Out-Null
    [IO.File]::WriteAllText($output, (@{passed=$passed;workspace=$testRoot;input='UI Automation patterns; no global keyboard injection';checks=$checks} | ConvertTo-Json -Depth 6))
    if (!$passed) { throw "UI integration failed: $output" }
    Write-Output "UI integration: $($checks.Count) checks passed. $output"
}
