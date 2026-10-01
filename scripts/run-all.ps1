# Builds the solution and starts every service + the Gateway in the background (Development).
# Logs: .run\logs\<project>.log   PIDs: .run\pids\<project>.pid   Stop: scripts\stop-all.ps1
# Infrastructure must already be up: docker compose up -d
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root '.run\logs'
$pids = Join-Path $root '.run\pids'
New-Item -ItemType Directory -Force $logs, $pids | Out-Null

dotnet build (Join-Path $root 'SmartCity.sln') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'

# Every web project under src\Gateway and src\Services (class libraries are skipped).
$projects = @(Get-ChildItem (Join-Path $root 'src\Gateway\*\*.csproj')) +
            @(Get-ChildItem (Join-Path $root 'src\Services\*\*\*.csproj'))

foreach ($csproj in $projects) {
    if (-not (Select-String -Path $csproj.FullName -Pattern 'Sdk="Microsoft.NET.Sdk.Web"' -Quiet)) { continue }
    $name = $csproj.BaseName
    $pidFile = Join-Path $pids "$name.pid"

    if (Test-Path $pidFile) {
        $existing = Get-Process -Id (Get-Content $pidFile) -ErrorAction SilentlyContinue
        if ($existing) { Write-Host "$name already running (pid $($existing.Id))"; continue }
    }

    # Run the built dll from the project folder (content root = appsettings location).
    $proc = Start-Process dotnet -ArgumentList "bin\Debug\net8.0\$name.dll" `
        -WorkingDirectory $csproj.DirectoryName -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logs "$name.log") `
        -RedirectStandardError (Join-Path $logs "$name.err.log")
    Set-Content -Path $pidFile -Value $proc.Id
    Write-Host "started $name (pid $($proc.Id))"
}
