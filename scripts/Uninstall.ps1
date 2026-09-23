param([string]$InstallDirectory = $PSScriptRoot, [switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$marker = Join-Path $directory 'scriblism-package.json'
$manifestPath = Join-Path $directory 'scriblism-files.json'
if (!(Test-Path $marker) -or !(Test-Path $manifestPath)) { throw 'No verified Scriblism package was found. Nothing was removed.' }
$package = [IO.File]::ReadAllText($marker) | ConvertFrom-Json
if ($package.product -ne 'Scriblism') { throw 'The package is not Scriblism.' }
$running = Get-Process -Name Scriblism -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $directory 'Scriblism.exe') }
if ($running) { throw 'Close Scriblism before uninstalling. No process was terminated.' }
if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Uninstalling through a directory link is not supported.' }
$links = Get-ChildItem -LiteralPath $directory -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
if ($links) { throw 'This installation contains links. Remove the links before uninstalling; no files were removed.' }
$files = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
$validated = @()
foreach ($entry in $files) {
    $path = [IO.Path]::GetFullPath((Join-Path $directory $entry.path))
    if (!$path.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe path in package manifest. Nothing was removed.' }
    $validated += @{ Path=$path; Hash=$entry.sha256 }
}
foreach ($entry in $validated) {
    if (Test-Path -LiteralPath $entry.Path -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -eq $entry.Hash) {
            Remove-Item -LiteralPath $entry.Path -Force
        } else { Write-Output "Kept modified file: $($entry.Path)" }
    }
}
Remove-Item -LiteralPath $manifestPath -Force
Get-ChildItem -LiteralPath $directory -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if (!(Get-ChildItem -LiteralPath $_.FullName -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $_.FullName }
}
if (!(Get-ChildItem -LiteralPath $directory -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $directory }
if (!$NoShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Scriblism.lnk'
    if (Test-Path $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq (Join-Path $directory 'Scriblism.exe')) { Remove-Item $shortcutPath }
    }
}
Write-Output 'Removed unchanged package files. User-added/modified files, settings and recovery backups were kept.'
