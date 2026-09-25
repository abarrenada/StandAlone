<#
  Undoes Enable-MongoRemote.ps1: MongoDB goes back to accepting connections from this machine
  only (bindIp 127.0.0.1) and the dry-run firewall rule is removed. Run as Administrator.
#>
param(
    [string]$ConfigPath = 'C:\Program Files\MongoDB\Server\8.3\bin\mongod.cfg',
    [string]$LogPath    = "$PSScriptRoot\mongo-remote.log"
)

$ErrorActionPreference = 'Stop'
Start-Transcript -Path $LogPath -Append | Out-Null
try {
    $cfg = Get-Content $ConfigPath -Raw
    $cfg = $cfg -replace '(?m)^(\s*bindIp:\s*).*$', '${1}127.0.0.1'
    Set-Content -Path $ConfigPath -Value $cfg -Encoding ascii -NoNewline
    Write-Host "bindIp set back to 127.0.0.1 in $ConfigPath"

    Get-NetFirewallRule -DisplayName 'StandAlone MongoDB (dry run)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Write-Host 'Firewall rule removed'

    Restart-Service MongoDB
    Start-Sleep -Seconds 3
    Get-NetTCPConnection -LocalPort 27017 -State Listen | Format-Table LocalAddress, LocalPort -AutoSize | Out-String | Write-Host
    Write-Host 'RESULT=OK'
}
catch {
    Write-Host "RESULT=FAILED $($_.Exception.Message)"
}
finally {
    Stop-Transcript | Out-Null
}
