<#
.SYNOPSIS
    Publishes Early Access Tracker and registers a per-user scheduled task that starts it in the tray.

.DESCRIPTION
    - Publishes a Release build to %LOCALAPPDATA%\Programs\EarlyAccessTracker (stops a running copy first).
    - Registers the task "Early Access Tracker" for the current user with two triggers:
        * At logon
        * Daily at -DailyTime (relaunches it if you exited it; a second copy just exits)
    - The app itself decides whether a Steam sync is due (at most once per calendar day),
      so the task can fire as often as it likes without causing extra Steam requests.

.EXAMPLE
    .\scripts\Install-ScheduledTask.ps1
    .\scripts\Install-ScheduledTask.ps1 -DailyTime 18:00 -StartNow
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\EarlyAccessTracker'),
    [string]$DailyTime = '09:00',
    [string]$TaskName = 'Early Access Tracker',
    [string]$TaskPath = '\MyTasks\',
    [switch]$StartNow,
    # Used by the Visual Studio "LocalInstall" publish profile, which has already published the files.
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\EarlyAccessTracker.App\EarlyAccessTracker.App.csproj'

if (-not $SkipPublish) {
    Get-Process -Name EarlyAccessTracker -ErrorAction SilentlyContinue | Stop-Process -Force

    Write-Host "Publishing to $InstallDir ..."
    dotnet publish $project -c Release -o $InstallDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$exe = Join-Path $InstallDir 'EarlyAccessTracker.exe'
if (-not (Test-Path $exe)) { throw "Not found: $exe" }
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$action = New-ScheduledTaskAction -Execute $exe -Argument '--background' -WorkingDirectory $InstallDir
$triggers = @(
    (New-ScheduledTaskTrigger -AtLogOn -User $user),
    (New-ScheduledTaskTrigger -Daily -At $DailyTime)
)
# Interactive principal: runs in your desktop session so the tray icon and toasts are visible.
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

# Remove copies registered in other folders (e.g. an earlier install at the root) so there's only one.
Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue |
    Where-Object TaskPath -ne $TaskPath |
    ForEach-Object {
        Write-Host "Removing old task '$($_.TaskPath)$($_.TaskName)'."
        Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -Confirm:$false
    }

Register-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Action $action -Trigger $triggers -Principal $principal `
    -Settings $settings -Description 'Tray app that tracks Steam games leaving Early Access.' -Force | Out-Null

Write-Host "Registered scheduled task '$TaskPath$TaskName' (at logon + daily at $DailyTime)."

if ($StartNow) {
    Start-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath
    Write-Host 'Started. Look for the "EA" icon in the system tray.'
}
