param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'Library/LocalizationVerification'
New-Item -ItemType Directory -Force $out | Out-Null
$mono=Join-Path $EditorData 'MonoBleedingEdge/bin/mono.exe'
$compiler=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
$netstandard=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
$json=Get-ChildItem "$root/Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll" | Select-Object -First 1
if(!$json){throw 'Restore Unity packages first.'}
Copy-Item -LiteralPath $json.FullName -Destination $out -Force
$sources=@(Get-ChildItem "$root/Assets/Game" -Filter *.cs -Recurse | ForEach-Object FullName)
& $mono $compiler /nologo /target:library "/out:$out/AfterSeoul.Game.dll" "/r:$out/Newtonsoft.Json.dll" "/r:$netstandard" $sources
if($LASTEXITCODE -ne 0){throw 'Game compilation failed'}
foreach($test in @('FirstRunVerification','LocaleVerification')){
 & $mono $compiler /nologo "/out:$out/$test.exe" "/r:$out/AfterSeoul.Game.dll" "/r:$out/Newtonsoft.Json.dll" "/r:$netstandard" "$PSScriptRoot/$test.cs"
 if($LASTEXITCODE -ne 0){throw "Test compilation failed: $test"}
 & $mono "$out/$test.exe" "$root/Assets/Resources/Locales/mobile"
 if($LASTEXITCODE -ne 0){throw "Test failed: $test"}
}
