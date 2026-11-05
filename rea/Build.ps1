param([string]$Configuration='Release',[switch]$NativeOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$core=Join-Path $root 'upstream/geolith'
if(!(Test-Path "$core/src/geo.c")){throw 'Initialize the pinned core with git submodule update --init --recursive first.'}
$expected='2dc88e3fa72ff404213a65d6eb5bb33baa687f73'
$actual=(& git -C $core rev-parse HEAD).Trim();if($actual-ne$expected){throw "Core revision mismatch: $actual"}
$build=Join-Path $PSScriptRoot 'obj/native';$bin=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $build,$bin | Out-Null
& node "$PSScriptRoot/patch-core.mjs" "$core/src/geo_lspc.c" "$build/geo_lspc.c"
if($LASTEXITCODE-ne0){throw 'Core observer patch failed.'}
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs=if(Test-Path $vswhere){& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath}else{'C:/Program Files/Microsoft Visual Studio/18/Community'}
$dev=Join-Path $vs 'Common7/Tools/VsDevCmd.bat';if(!(Test-Path $dev)){throw 'Visual Studio C++ build tools are required.'}
$sources=@(Get-ChildItem "$core/src/*.c" | Where-Object Name -notin @('geo_chd.c','geo_lspc.c','geo_m68k.c') | ForEach-Object FullName)
$sources+="$build/geo_m68k.c"
$sources+=@("$build/geo_lspc.c","$PSScriptRoot/native/bridge.c","$core/src/m68k/m68kcpu.c","$core/src/m68k/m68kops.c","$core/src/z80/z80.c","$core/src/ymfm/ymfm_adpcm.c","$build/ymfm_opn.c","$core/src/ymfm/ymfm_ssg.c","$core/deps/miniz/miniz.c","$core/deps/speex/resample.c")
$env:INCLUDE="$core/src/ymfm;$env:INCLUDE"
$compileOptions=@('/nologo','/LD','/TC','/std:c17','/O2','/MT','/D_CRT_SECURE_NO_WARNINGS','/D_USE_MATH_DEFINES','/DWIN32','/D_CRT_NONSTDC_NO_DEPRECATE','/D__BYTE_ORDER__=1234','/D__ORDER_LITTLE_ENDIAN__=1234','/D__ORDER_BIG_ENDIAN__=4321',"/I`"$core/src`"","/I`"$core/deps`"","/I`"$core/deps/libretro-common/include`"","/I`"$core/deps/miniz`"","/I`"$PSScriptRoot/native`"", '/wd4244','/wd4267','/wd4018','/wd4996')
$compileOptions+=($sources | ForEach-Object {"`"$_`""});$compileOptions+="/link /OUT:`"$bin/neo-reaper-core.dll`" /INCREMENTAL:NO"
$response=Join-Path $build 'compile.rsp';Set-Content -LiteralPath $response -Value ($compileOptions-join "`r`n") -Encoding utf8NoBOM
$cmd=Join-Path $build 'compile.cmd';Set-Content -LiteralPath $cmd -Value "@echo off`r`ncall `"$dev`" -arch=x64 -host_arch=x64 >nul`r`nif errorlevel 1 exit /b 1`r`ncd /d `"$build`"`r`ncl @`"$response`"`r`n" -Encoding ascii
& $env:ComSpec /d /c $cmd
if($LASTEXITCODE-ne0){throw 'Native core compilation failed.'}
if(!$NativeOnly){& dotnet build "$PSScriptRoot/host/NeoReaper.csproj" -c $Configuration -o $bin;if($LASTEXITCODE-ne0){throw 'Host build failed.'}}
$manifest=[ordered]@{product='NEO REAPER - Geolith';coreRevision=$expected;system='Windows x64';nativeDllSha256=(Get-FileHash "$bin/neo-reaper-core.dll" -Algorithm SHA256).Hash.ToLowerInvariant();configuration=$Configuration;stateFormat='NR-state-4-MSVC-x64';nativeCompiler=(Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name);dotnetSdk=(& dotnet --version)}
if(Test-Path "$bin/NeoReaper.dll"){$manifest.hostSha256=(Get-FileHash "$bin/NeoReaper.dll" -Algorithm SHA256).Hash.ToLowerInvariant()}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$bin/build-manifest.json" -Encoding utf8NoBOM
Write-Output "Built NEO REAPER - Geolith in $bin"
