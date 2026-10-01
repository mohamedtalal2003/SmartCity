# Stops the services started by scripts\run-all.ps1.
$root = Split-Path -Parent $PSScriptRoot
$pids = Join-Path $root '.run\pids'

foreach ($pidFile in @(Get-ChildItem (Join-Path $pids '*.pid') -ErrorAction SilentlyContinue)) {
    $name = $pidFile.BaseName
    $proc = Get-Process -Id (Get-Content $pidFile.FullName) -ErrorAction SilentlyContinue
    if ($proc) {
        Stop-Process -Id $proc.Id -Force
        Write-Host "stopped $name (pid $($proc.Id))"
    } else {
        Write-Host "$name was not running"
    }
    Remove-Item $pidFile.FullName
}
