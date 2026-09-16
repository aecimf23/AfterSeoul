param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'Library/MailRegression'
New-Item -ItemType Directory -Force $out | Out-Null
$json=(Get-ChildItem "$root/Library/PackageCache" -Recurse -Filter Newtonsoft.Json.dll | Select-Object -First 1).FullName
$nunit=(Get-ChildItem "$root/Library/PackageCache" -Recurse -Filter nunit.framework.dll | Select-Object -First 1).FullName
$engine="$EditorData/Managed/UnityEngine/UnityEngine.CoreModule.dll"
Copy-Item -LiteralPath $json,$nunit,$engine -Destination $out -Force
$sources=@(Get-ChildItem "$root/Assets/Game" -Recurse -Filter *.cs | ForEach-Object FullName)
$sources+=@('OutboxTests','OfflineResolverTests','SessionAndDataTests') | ForEach-Object {"$root/Assets/Tests/EditMode/$_.cs"}
& "$EditorData/MonoBleedingEdge/bin/mono.exe" "$EditorData/MonoBleedingEdge/lib/mono/4.5/csc.exe" /nologo /warn:0 "/out:$out/tests.exe" "/r:$json" "/r:$nunit" "/r:$engine" "/r:$EditorData/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll" @sources "$PSScriptRoot/Run-MailRegression.cs"
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
& "$EditorData/MonoBleedingEdge/bin/mono.exe" "$out/tests.exe" > "$out/results.xml"
if($LASTEXITCODE -ne 0){Get-Content "$out/results.xml"; throw 'Regression failed'}
[xml]$result=Get-Content "$out/results.xml"
$result.'test-suite' | Select-Object total,passed,failed,result
