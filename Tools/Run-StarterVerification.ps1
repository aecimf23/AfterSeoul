param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'Library/StarterVerification'
New-Item -ItemType Directory -Force $out | Out-Null
$mono=Join-Path $EditorData 'MonoBleedingEdge/bin/mono.exe'
$compiler=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
$netstandard=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
$json=Get-ChildItem "$root/Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll" | Select-Object -First 1
if(!$json){throw 'Restore Unity packages first.'}
Copy-Item -LiteralPath $json.FullName -Destination $out -Force
$sources=@(Get-ChildItem "$root/Assets/Game" -Filter *.cs -Recurse | ForEach-Object FullName)
& $mono $compiler /nologo /nowarn:0649 "/out:$out/StarterVerification.exe" "/r:$out/Newtonsoft.Json.dll" "/r:$netstandard" $sources "$PSScriptRoot/StarterVerification.cs"
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
& $mono "$out/StarterVerification.exe" "$root/Assets/StreamingAssets/Data"
if($LASTEXITCODE -ne 0){throw 'Starter verification failed'}
