<#
.SYNOPSIS
    Removes the scheduled task and the published app. Leaves the database
    (%LOCALAPPDATA%\EarlyAccessTracker) and your secrets file in place unless -RemoveData is given.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\EarlyAccessTracker'),
    [string]$TaskName = 'Early Access Tracker',
    [string]$TaskPath = '\MyTasks\',
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Get-Process -Name EarlyAccessTracker -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'EarlyAccessTracker'
    if (Test-Path $data) { Remove-Item -Recurse -Force $data }
}
Write-Host 'Uninstalled.'
