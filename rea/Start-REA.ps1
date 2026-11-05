#Requires -Version 7.0
param([string]$Rom,[string]$Firmware,[ValidateSet('AES','MVS')][string]$System='AES',[ValidateRange(0,3)][int]$Region=0,[ValidateRange(1024,65535)][int]$Port=8910,[string]$DataDirectory=(Join-Path $PSScriptRoot 'session'),[switch]$Diagnostic,[switch]$Build,[switch]$Wait)
$ErrorActionPreference='Stop'
if($Build -or !(Test-Path "$PSScriptRoot/bin/NeoReaper.dll")){& "$PSScriptRoot/Build.ps1";if($LASTEXITCODE-ne0){throw 'Build failed'}}
if($Diagnostic){& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' "$PSScriptRoot/diagnostic.py";if($LASTEXITCODE-ne0){throw 'Diagnostic generation failed'};$Rom="$PSScriptRoot/test-output/diagnostic/original.neo";$Firmware="$PSScriptRoot/test-output/diagnostic/original-firmware.zip"}
if(!$Rom -or !$Firmware){throw 'Supply -Rom cartridge.neo -Firmware firmware.zip, or -Diagnostic for the original test-only fixtures.'}
$Rom=(Resolve-Path -LiteralPath $Rom).Path;$Firmware=(Resolve-Path -LiteralPath $Firmware).Path
$probe=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,$Port)
try{$probe.Start()}catch{throw "Port $Port is occupied. Choose -Port; no existing process was stopped."}finally{$probe.Stop()}
$hostArgs=@("$PSScriptRoot/bin/NeoReaper.dll",'--rom',$Rom,'--firmware',$Firmware,'--system',$System,'--region',"$Region",'--port',"$Port",'--data',[IO.Path]::GetFullPath($DataDirectory),'--web',"$PSScriptRoot/public")
Write-Host "NEO REAPER - Geolith: http://127.0.0.1:$Port/"
if($Wait){& dotnet @hostArgs;exit $LASTEXITCODE}
# ProcessStartInfo.ArgumentList preserves spaces and Unicode without shell quoting.
$start=[Diagnostics.ProcessStartInfo]::new('dotnet');$start.UseShellExecute=$false;$start.CreateNoWindow=$true;foreach($a in $hostArgs){$start.ArgumentList.Add($a)}
$process=[Diagnostics.Process]::Start($start)
Write-Output ([pscustomobject]@{ProcessId=$process.Id;Url="http://127.0.0.1:$Port/";System=$System;Data=$DataDirectory})
