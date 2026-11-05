#Requires -Version 7.0
param([string]$Rom,[string]$Firmware,[ValidateSet('AES','MVS')][string]$System='AES',[ValidateRange(0,3)][int]$Region=0,[ValidateRange(1024,65535)][int]$Port=8910,[string]$DataDirectory=(Join-Path $PSScriptRoot 'session'),[string]$SpriteHelpers=(Join-Path $PSScriptRoot 'sprite-helpers'),[string]$RetroArch,[string]$Python,[switch]$Diagnostic,[switch]$Build,[switch]$Wait)
$ErrorActionPreference='Stop'
if($RetroArch){$RetroArch=(Resolve-Path -LiteralPath $RetroArch).Path}
if($Build -or !(Test-Path "$PSScriptRoot/bin/NeoReaper.dll") -or ($RetroArch -and !(Test-Path "$PSScriptRoot/bin/neo-reaper-libretro.dll"))){
    if(!(Test-Path "$PSScriptRoot/Build.ps1")){throw 'Use a complete release ZIP, or build from a source checkout.'}
    & "$PSScriptRoot/Build.ps1"
    if($LASTEXITCODE-ne0){throw 'Build failed'}
}
if($Diagnostic){
    if(!$Python){
        $installed=Join-Path $env:LOCALAPPDATA 'Python/bin/python.exe'
        if(Test-Path -LiteralPath $installed){$Python=$installed}
        else{$Python=(Get-Command python.exe -CommandType Application -All -ErrorAction SilentlyContinue |
            Where-Object Source -NotMatch '[\\/]WindowsApps[\\/]' | Select-Object -First 1).Source}
    }
    if(!$Python){throw 'Supply -Python with the path to an installed Python interpreter for diagnostics.'}
    $Python=(Resolve-Path -LiteralPath $Python).Path
    if($Python -match '[\\/]WindowsApps[\\/]'){throw 'Supply -Python with an installed interpreter path.'}
    & $Python "$PSScriptRoot/diagnostic.py"
    if($LASTEXITCODE-ne0){throw 'Diagnostic generation failed'}
    $Rom="$PSScriptRoot/test-output/diagnostic/original.neo"
    $Firmware="$PSScriptRoot/test-output/diagnostic/original-firmware.zip"
}
if(!$Rom -or !$Firmware){throw 'Supply -Rom cartridge.neo -Firmware firmware.zip, or -Diagnostic for the original test-only fixtures.'}
$Rom=(Resolve-Path -LiteralPath $Rom).Path;$Firmware=(Resolve-Path -LiteralPath $Firmware).Path
$probe=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,$Port)
try{$probe.Start()}catch{throw "Port $Port is occupied. Choose -Port; no existing process was stopped."}finally{$probe.Stop()}
$hostArgs=@("$PSScriptRoot/bin/NeoReaper.dll",'--rom',$Rom,'--firmware',$Firmware,'--system',$System,'--region',"$Region",'--port',"$Port",'--data',[IO.Path]::GetFullPath($DataDirectory),'--sprite-helpers',[IO.Path]::GetFullPath($SpriteHelpers),'--web',"$PSScriptRoot/public")
if($RetroArch){$retroPipe="neo-reaper-$([Guid]::NewGuid().ToString('N'))";$hostArgs+=@('--retro-pipe',$retroPipe)}
Write-Host "NEO REAPER - Geolith: http://127.0.0.1:$Port/"
if($Wait -and !$RetroArch){& dotnet @hostArgs;exit $LASTEXITCODE}
# ProcessStartInfo.ArgumentList preserves spaces and Unicode without shell quoting.
$start=[Diagnostics.ProcessStartInfo]::new('dotnet');$start.UseShellExecute=$false;$start.CreateNoWindow=$true;foreach($a in $hostArgs){$start.ArgumentList.Add($a)}
$process=[Diagnostics.Process]::Start($start)
Write-Output ([pscustomobject]@{ProcessId=$process.Id;Url="http://127.0.0.1:$Port/";System=$System;Data=$DataDirectory})

if($RetroArch){
    try {
        $frontend=[Diagnostics.ProcessStartInfo]::new($RetroArch)
        $frontend.UseShellExecute=$false
        $frontend.WorkingDirectory=Split-Path $RetroArch
        $frontend.Environment['NR_RETRO_PIPE']=$retroPipe
        $frontend.Environment['NR_RETRO_ROM']=$Rom
        $config=Join-Path $frontend.WorkingDirectory 'retroarch.cfg'
        if(Test-Path -LiteralPath $config){foreach($a in @('--config',$config)){$frontend.ArgumentList.Add($a)}}
        $log=Join-Path $frontend.WorkingDirectory 'rea-native.log'
        foreach($a in @('--verbose','--log-file',$log,'--libretro',"$PSScriptRoot/bin/neo-reaper-libretro.dll",$Rom)){$frontend.ArgumentList.Add($a)}
        $window=[Diagnostics.Process]::Start($frontend)
        Write-Output ([pscustomobject]@{RetroArchProcessId=$window.Id})
        $window.WaitForExit()
        if($window.ExitCode -ne 0){throw "RetroArch exited with code $($window.ExitCode). See $log"}
    } finally {
        if(!$process.HasExited){$process.Kill()}
    }
}
