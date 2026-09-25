<#
  Opens this machine's local MongoDB (the StandAlone data lake) to other stations on the plant
  network, for the dry run. NO AUTHENTICATION — anyone who can reach port 27017 can read/write.
  Undo with Disable-MongoRemote.ps1 as soon as the dry run is over.

  Must run as Administrator (edits Program Files config, firewall, restarts the service).
  Other stations then use:  mongodb://<this computer's name>:27017
#>
param(
    [string]$ConfigPath   = 'C:\Program Files\MongoDB\Server\8.3\bin\mongod.cfg',
    [string]$AllowFrom    = '10.121.0.0/16',   # plant network; narrow to one station's IP if known
    [string]$LogPath      = "$PSScriptRoot\mongo-remote.log"
)

$ErrorActionPreference = 'Stop'
Start-Transcript -Path $LogPath -Append | Out-Null
try {
    $backup = "$ConfigPath.localhost-only.bak"
    if (-not (Test-Path $backup)) { Copy-Item $ConfigPath $backup }

    $cfg = Get-Content $ConfigPath -Raw
    $cfg = $cfg -replace '(?m)^(\s*bindIp:\s*).*$', '${1}0.0.0.0'
    Set-Content -Path $ConfigPath -Value $cfg -Encoding ascii -NoNewline
    Write-Host "bindIp set to 0.0.0.0 in $ConfigPath (backup: $backup)"

    # Domain-profile firewall is disabled by corporate policy here, but add a scoped rule so this
    # keeps working if the NIC is ever classified Private/Public.
    Get-NetFirewallRule -DisplayName 'StandAlone MongoDB (dry run)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName 'StandAlone MongoDB (dry run)' -Direction Inbound -Protocol TCP `
        -LocalPort 27017 -RemoteAddress $AllowFrom -Action Allow -Profile Any | Out-Null
    Write-Host "Firewall rule added: TCP 27017 from $AllowFrom"

    Restart-Service MongoDB
    Start-Sleep -Seconds 3
    Get-Service MongoDB | Format-Table Name, Status -AutoSize | Out-String | Write-Host
    Get-NetTCPConnection -LocalPort 27017 -State Listen | Format-Table LocalAddress, LocalPort -AutoSize | Out-String | Write-Host
    Write-Host "Other stations: mongodb://$($env:COMPUTERNAME).$((Get-CimInstance Win32_ComputerSystem).Domain):27017"
    Write-Host 'RESULT=OK'
}
catch {
    Write-Host "RESULT=FAILED $($_.Exception.Message)"
}
finally {
    Stop-Transcript | Out-Null
}
