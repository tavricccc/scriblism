param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$NativeTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$local = Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
Push-Location $root
try {
    & $dotnet build Scriblism.slnx -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet test tests/Scriblism.Core.Tests/Scriblism.Core.Tests.csproj -c $Configuration --no-build --logger 'trx;LogFileName=core.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ($NativeTests) {
        $oldState = $env:SCRIBLISM_STATE_ROOT
        try {
            $env:SCRIBLISM_STATE_ROOT = Join-Path $root 'artifacts/native-test-profile'
            $exe = Join-Path $root "src/Scriblism.App/bin/$Configuration/net10.0-windows10.0.26100.0/win-x64/Scriblism.exe"
            $output = Join-Path $root 'artifacts/test-results/native.json'
            if (Test-Path $output) { Remove-Item $output }
            $process = Start-Process $exe -ArgumentList @('--native-test', "`"$output`"") -PassThru
            if (!$process.WaitForExit(90000)) { throw "Native tests timed out (PID $($process.Id)). Inspect the window before retrying." }
            if (!(Test-Path $output)) { throw 'Native tests did not produce a result.' }
            $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
            if (!$result.passed) { throw "Native tests failed: $output" }
            Write-Output "Native integration: $($result.checks.Count) checks passed."
        } finally { $env:SCRIBLISM_STATE_ROOT = $oldState }
    }
} finally { Pop-Location }
