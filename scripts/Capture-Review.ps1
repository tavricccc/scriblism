param([string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root = Split-Path $PSScriptRoot -Parent
if (!$Executable) { $Executable = Join-Path $root 'artifacts/Scriblism-win-x64/Scriblism.exe' }
$output = Join-Path $root '.impeccable/review'
New-Item -ItemType Directory -Force $output | Out-Null
$samples = Join-Path $root 'artifacts/capture-fixtures'
New-Item -ItemType Directory -Force $samples | Out-Null
[IO.File]::WriteAllText((Join-Path $samples 'greeting.py'), "def greet(name):`n    return f'Hello, {name}'`n")
[IO.File]::WriteAllText((Join-Path $samples '開始使用.md'), "# Markdown`n`n這是一份編輯器檢查文件。`n")
$oldState = $env:SCRIBLISM_STATE_ROOT
$script:app = $null
function Launch([string]$Theme,[string[]]$Files) {
    $env:SCRIBLISM_STATE_ROOT = Join-Path $root ('artifacts/capture-' + $Theme.ToLowerInvariant())
    New-Item -ItemType Directory -Force $env:SCRIBLISM_STATE_ROOT | Out-Null
    [IO.File]::WriteAllText((Join-Path $env:SCRIBLISM_STATE_ROOT 'settings.json'),(@{Theme=$Theme;FontSize=15;WordWrap=$true;SidebarVisible=$true;RecentFiles=@()} | ConvertTo-Json))
    $script:app = Start-Process $Executable -ArgumentList @($Files | ForEach-Object { '"' + $_ + '"' }) -PassThru
    Start-Sleep -Seconds 2
    $script:tree = [Windows.Automation.AutomationElement]::FromHandle($script:app.MainWindowHandle)
}
function Element([string]$Name,[Windows.Automation.ControlType]$Type) {
    $nodes = $script:tree.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Name))
    foreach ($node in $nodes) { if (!$node.Current.IsOffscreen -and (!$Type -or $node.Current.ControlType -eq $Type)) { return $node } }
    throw "Not found: $Name"
}
function Menu([string]$Top,[string]$Item) {
    (Element $Top ([Windows.Automation.ControlType]::MenuItem)).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 200
    (Element $Item ([Windows.Automation.ControlType]::MenuItem)).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}
function Select-Document([string]$Name) {
    (Element $Name ([Windows.Automation.ControlType]::TabItem)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 350
}
function Capture([string]$Name,[int]$Width=1220,[int]$Height=840) {
    & (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -ProcessId $script:app.Id -Output (Join-Path $output $Name) -Width $Width -Height $Height | Out-Null
}
function Close-App {
    $null=$script:app.CloseMainWindow()
    if (!$script:app.WaitForExit(10000)) { throw "Capture window did not close; inspect PID $($script:app.Id)" }
}
try {
    Launch 'Light' @((Join-Path $samples 'greeting.py'),(Join-Path $samples '開始使用.md'))
    Capture 'final-markdown-light.png'
    Menu '編輯' '取代'
    (Element '搜尋文字').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Markdown')
    Start-Sleep -Milliseconds 400
    Capture 'final-floating-replace.png'
    Capture 'final-narrow-replace.png' 820 720
    (Element '關閉搜尋').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Select-Document 'greeting.py'
    Capture 'final-code-light.png'
    $script:app.Refresh()
    $modules = @($script:app.Modules | Where-Object { $_.ModuleName -in @('coreclr.dll','Microsoft.UI.Xaml.dll') } | Select-Object ModuleName,FileName)
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($script:app.Id)" | Select-Object Name,ExecutablePath)
    [IO.File]::WriteAllText((Join-Path $root 'artifacts/test-results/runtime.json'),(@{privateBytes=$script:app.PrivateMemorySize64;workingSetBytes=$script:app.WorkingSet64;runtimeModules=$modules;childProcesses=$children} | ConvertTo-Json -Depth 5))
    Close-App
    Launch 'Dark' @((Join-Path $samples '開始使用.md'),(Join-Path $samples 'missing-file.txt'))
    Capture 'final-dark-toast.png'
    Close-App
    Write-Output "Captured five native views in $output"
} finally { $env:SCRIBLISM_STATE_ROOT=$oldState }
