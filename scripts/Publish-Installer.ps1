# One version for the whole product: the installer is never released separately from the
# app it installs, so a second number would only ever be a thing to keep in sync.
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installerRoot = Join-Path $projectRoot 'artifacts/installer'
# One file, and it is the release. There is no folder beside it to keep, and no second
# installer to choose between: both layouts travel inside it and it decides which to install.
$releaseExe = Join-Path $installerRoot 'Scriblism.Setup.exe'
$work = Join-Path $installerRoot ('.build-' + [guid]::NewGuid().ToString('N'))
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
function Assert-SafeTree([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath($installerRoot) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe build path: $resolved" }
    for ($ancestor = $resolved; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build path contains a reparse point.' }
    }
    if (Test-Path -LiteralPath $resolved) {
        foreach ($item in Get-ChildItem -LiteralPath $resolved -Recurse -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Build contents contain a reparse point.' }
        }
    }
}

# Builds one complete installation layout: the app and the setup interface, published
# together so they share one copy of the .NET runtime between them.
function New-Layout([string]$Destination, [bool]$SelfContainedSdk) {
    $sdk = if ($SelfContainedSdk) { 'true' } else { 'false' }
    foreach ($project in @('src/Scriblism.App/Scriblism.App.csproj', 'src/Scriblism.Setup/Scriblism.Setup.csproj', 'src/Scriblism.Uninstall/Scriblism.Uninstall.csproj')) {
        # Switching WindowsAppSDKSelfContained does not invalidate every incremental PRI input.
        # A shared-layout PRI reused in the standalone layout omits WinUI theme resources and
        # the app fails before its first window. Rebuild resources for each layout explicitly.
        Invoke-Dotnet @('clean', $project, '-c', 'Release', '-p:Platform=x64', "-p:WindowsAppSDKSelfContained=$sdk")
        Invoke-Dotnet @('publish', $project, '-c', 'Release', '-p:Platform=x64', "-p:AppVersion=$Version", "-p:WindowsAppSDKSelfContained=$sdk", '-o', $Destination)
    }
    # The Windows App SDK metapackage drags its machine-learning stack along with everything
    # else. Scriblism calls none of the Windows AI APIs, and these two are loaded only when
    # something does, so they are 38 MB of an installer that exists to download files. There is
    # no supported property to exclude them, hence the deletion here rather than in a project.
    foreach ($unused in @('onnxruntime.dll', 'DirectML.dll')) {
        $path = Join-Path $Destination $unused
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }

    # coreclr.dll being present is not proof that each executable carries its own runtime: the
    # files are merged from several publishes, so one framework-dependent executable can sit
    # among them and only fail on a machine without the matching .NET installed.
    foreach ($runtimeConfig in Get-ChildItem -LiteralPath $Destination -Filter 'Scriblism.*.runtimeconfig.json' -File) {
        $options = (Get-Content -LiteralPath $runtimeConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions
        if ($options.PSObject.Properties.Name -contains 'framework' -or
            $options.PSObject.Properties.Name -contains 'frameworks') {
            throw "$($runtimeConfig.Name) is framework-dependent; publish it self-contained."
        }
    }

    $files = [ordered]@{}
    $destPrefix = $Destination.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $Destination -File -Recurse | Sort-Object FullName) {
        $rel = if ($file.FullName.StartsWith($destPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($destPrefix.Length)
        } else {
            $file.FullName
        }
        $files[$rel] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    # Microsoft.UI.Xaml.dll only lives in the installation when the SDK is copied in, so the
    # shared layout is checked against the managed projection that both layouts carry.
    $expected = @('Scriblism.exe', 'Scriblism.Setup.exe', 'Uninstall.exe', 'coreclr.dll', 'Microsoft.WinUI.dll')
    $expected += if ($SelfContainedSdk) { 'Microsoft.UI.Xaml.dll' } else { 'Microsoft.WindowsAppRuntime.Bootstrap.dll' }
    foreach ($required in $expected) {
        if (!$files.Contains($required)) { throw "Missing $required" }
    }
    @{ Product = 'Scriblism'; Version = $Version; Files = $files } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $Destination 'scriblism-install.json') -Encoding utf8
}

Push-Location $projectRoot
try {
    Assert-SafeTree $work

    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -NativeTests

    $sharedTree = Join-Path $work 'shared'
    $standaloneTree = Join-Path $work 'standalone'
    New-Layout $sharedTree $false
    New-Layout $standaloneTree $true

    # The two layouts overlap almost entirely -- the second is the first plus the Windows App
    # SDK binaries -- so a file present in both with the same contents is stored once. Shipping
    # both layouts whole would add a hundred megabytes to the download for a second copy of
    # files that are byte for byte what the first copy already carries.
    $payload = Join-Path $work 'payload'
    $sharedHashes = @{}
    $sharedPrefix = $sharedTree.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $sharedTree -File -Recurse) {
        $rel = if ($file.FullName.StartsWith($sharedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($sharedPrefix.Length)
        } else {
            $file.FullName
        }
        $sharedHashes[$rel] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    $shared = 0
    $standalonePrefix = $standaloneTree.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $standaloneTree -File -Recurse) {
        $relative = if ($file.FullName.StartsWith($standalonePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($standalonePrefix.Length)
        } else {
            $file.FullName
        }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $section = 'standalone'
        if ($sharedHashes[$relative] -eq $hash) { $shared++; $section = 'common' }
        $destination = Join-Path $payload (Join-Path $section $relative)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    foreach ($file in Get-ChildItem -LiteralPath $sharedTree -File -Recurse) {
        $relative = if ($file.FullName.StartsWith($sharedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($sharedPrefix.Length)
        } else {
            $file.FullName
        }
        if (Test-Path -LiteralPath (Join-Path $payload (Join-Path 'common' $relative))) { continue }
        $destination = Join-Path $payload (Join-Path 'shared' $relative)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }

    # The MSIX packages the installer registers when the shared layout is chosen. Carried rather
    # than downloaded: an installer that needs the network to reach a usable state fails in a
    # place nobody can diagnose, and the point of the shared runtime is where the files end up,
    # not where they came from. They are never installed, so they sit outside both layouts.
    $assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src/Scriblism.App/obj/project.assets.json') -Raw | ConvertFrom-Json
    $runtimePackage = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.WindowsAppSDK.Runtime/*' } | Select-Object -First 1
    if (!$runtimePackage) { throw 'Microsoft.WindowsAppSDK.Runtime was not restored; cannot find the MSIX packages.' }
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $msixSource = Join-Path $nugetRoot ($runtimePackage.ToLowerInvariant() + '/tools/MSIX/win10-x64')
    if (!(Test-Path -LiteralPath $msixSource)) { throw "MSIX packages not found at $msixSource" }
    $msixTarget = Join-Path $payload 'runtime'
    [IO.Directory]::CreateDirectory($msixTarget) | Out-Null
    foreach ($msix in Get-ChildItem -LiteralPath $msixSource -Filter '*.msix' -File) {
        Copy-Item -LiteralPath $msix.FullName -Destination $msixTarget
    }
    if (!(Test-Path -LiteralPath (Join-Path $msixTarget 'Microsoft.WindowsAppRuntime.2.msix'))) {
        throw 'Missing the Windows App Runtime framework package.'
    }

    $archiveFile = Join-Path $work 'payload.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archiveFile, [IO.Compression.CompressionLevel]::Optimal, $false)

    # The launcher carries its own .NET runtime: it runs before anything has been unpacked, on a
    # machine that may have no .NET at all.
    $launcher = Join-Path $work 'launcher'
    Invoke-Dotnet @('publish', 'src/Scriblism.Bootstrap/Scriblism.Bootstrap.csproj', '-c', 'Release', "-p:AppVersion=$Version", '-o', $launcher)

    # Appended after the published executable rather than embedded in it: the payload is most of
    # two hundred megabytes of already-compressed binaries, and putting that through the compiler
    # and the single-file bundler costs minutes and a great deal of memory for a byte-identical
    # result. The single-file bundle is located from a header written into the host at publish
    # time, so trailing bytes do not disturb it. Payload.cs reads the trailer back.
    $staged = Join-Path $work 'Scriblism.Setup.exe'
    Copy-Item -LiteralPath (Join-Path $launcher 'Scriblism.Setup.exe') -Destination $staged
    $source = [IO.File]::OpenRead($archiveFile)
    $output = [IO.File]::Open($staged, [IO.FileMode]::Append)
    try {
        $length = $source.Length
        $source.CopyTo($output)
        $output.Write([BitConverter]::GetBytes([long]$length), 0, 8)
        $output.Write([Text.Encoding]::ASCII.GetBytes('SCRIBLISM-PAYLOAD'), 0, 17)
    } finally {
        $output.Dispose()
        $source.Dispose()
    }

    # Finish the entire release before touching the published installer. Old ones are archived.
    if (Test-Path -LiteralPath $releaseExe) {
        $history = Join-Path $installerRoot 'history'
        [IO.Directory]::CreateDirectory($history) | Out-Null
        $archived = Join-Path $history ('Scriblism.Setup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.exe')
        Move-Item -LiteralPath $releaseExe -Destination $archived
        Write-Output "Previous installer preserved: $archived"
    }
    [IO.Directory]::CreateDirectory($installerRoot) | Out-Null
    Move-Item -LiteralPath $staged -Destination $releaseExe

    $megabytes = [math]::Round((Get-Item -LiteralPath $releaseExe).Length / 1MB, 1)
    Write-Output "Installer: $releaseExe ($megabytes MB, version $Version)"
    Write-Output "Stored once instead of twice: $shared files shared between the two layouts."
    Write-Output 'It registers the shared Windows App SDK runtime when that is wanted and possible, and installs the self-contained layout when it is not.'
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $work) {
        Assert-SafeTree $work
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
