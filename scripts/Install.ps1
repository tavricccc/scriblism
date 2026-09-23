param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/Scriblism'), [switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
if (!(Test-Path (Join-Path $source 'scriblism-package.json')) -or !(Test-Path (Join-Path $source 'Scriblism.exe'))) {
    throw 'Run Install.ps1 from the published Scriblism package, not from the source scripts folder.'
}
$destination = [IO.Path]::GetFullPath($InstallDirectory)
if ($destination.TrimEnd('\') -eq $source.TrimEnd('\')) { throw 'Choose an installation folder different from the portable package folder.' }
if (Test-Path $destination) {
    $marker = Join-Path $destination 'scriblism-package.json'
    if (!(Test-Path $marker)) { throw 'The destination already exists and is not a Scriblism installation. Nothing was changed.' }
    $package = [IO.File]::ReadAllText($marker) | ConvertFrom-Json
    if ($package.product -ne 'Scriblism') { throw 'Installation marker does not belong to Scriblism.' }
}
$running = Get-Process -Name Scriblism -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $destination 'Scriblism.exe') }
if ($running) { throw 'Close Scriblism before installing. Unsaved documents are not closed automatically.' }
$parent = Split-Path $destination -Parent
New-Item -ItemType Directory -Force $parent | Out-Null
$staging = Join-Path $parent ('Scriblism-staging-' + [Guid]::NewGuid().ToString('N'))
$backup = $destination + '.previous-' + [Guid]::NewGuid().ToString('N')
try {
    New-Item -ItemType Directory $staging | Out-Null
    Copy-Item (Join-Path $source '*') $staging -Recurse -Force
    if (Test-Path $destination) { Move-Item $destination $backup }
    try { Move-Item $staging $destination }
    catch { if (Test-Path $backup) { Move-Item $backup $destination }; throw }
    if (!$NoShortcut) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Scriblism.lnk'
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $destination 'Scriblism.exe'
        $shortcut.WorkingDirectory = $destination
        $shortcut.IconLocation = Join-Path $destination 'Assets/Scriblism.ico'
        $shortcut.Description = 'Native code and Markdown editor'
        $shortcut.Save()
    }
    if (Test-Path $backup) { Write-Output "Previous installation retained (including any user-added files): $backup" }
    Write-Output "Installed: $destination/Scriblism.exe"
    if (!$NoShortcut) { Write-Output 'A Start menu shortcut was created.' }
    Write-Output 'Default file associations were not changed.'
} finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
