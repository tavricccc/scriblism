param([switch]$SkipTests, [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$SkipTests) { & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -NativeTests }
$local = Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$destination = Join-Path $root 'artifacts/Scriblism-win-x64'
$staging = Join-Path $root ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    & $dotnet publish src/Scriblism.App/Scriblism.App.csproj -c Release -r win-x64 --self-contained true -p:AppVersion=$Version -p:WindowsAppSDKSelfContained=true -p:DebugType=None -p:DebugSymbols=false -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item (Join-Path $root 'third-party') (Join-Path $staging 'third-party') -Recurse
    Copy-Item (Join-Path $root 'README.md'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') $staging
    Copy-Item (Join-Path $PSScriptRoot 'Install.ps1'),(Join-Path $PSScriptRoot 'Uninstall.ps1') $staging
    [IO.File]::WriteAllText((Join-Path $staging 'scriblism-package.json'), (@{ product = 'Scriblism'; version = $Version; architecture = 'x64'; selfContained = $true } | ConvertTo-Json -Compress))
    $files = @(Get-ChildItem $staging -File -Recurse | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($staging, $_.FullName); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
    [IO.File]::WriteAllText((Join-Path $staging 'scriblism-files.json'), ($files | ConvertTo-Json -Depth 4))
    if (Test-Path $destination) {
        $history = Join-Path $root ('artifacts/history/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force (Split-Path $history -Parent) | Out-Null
        Move-Item $destination $history
    }
    Move-Item $staging $destination
    $archiveName = "Scriblism-$Version-win-x64.zip"
    $archive = Join-Path $root "artifacts/$archiveName"
    Compress-Archive -Path "$destination/*" -DestinationPath $archive -Force -CompressionLevel Optimal
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(($archive + '.sha256'), "$hash  $archiveName`n")
    Write-Output "Application: $destination/Scriblism.exe"
    Write-Output "Archive: $archive"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
