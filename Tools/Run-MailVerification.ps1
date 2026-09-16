param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'Library/MailVerification'
New-Item -ItemType Directory -Force $out | Out-Null
$json=(Get-ChildItem "$root/Library/PackageCache" -Recurse -Filter Newtonsoft.Json.dll | Select-Object -First 1).FullName
Copy-Item -LiteralPath $json -Destination $out -Force
$sources=@(Get-ChildItem "$root/Assets/Game" -Recurse -Filter *.cs | ForEach-Object FullName)
& "$EditorData/MonoBleedingEdge/bin/mono.exe" "$EditorData/MonoBleedingEdge/lib/mono/4.5/csc.exe" /nologo "/out:$out/tests.exe" "/r:$json" "/r:$EditorData/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll" @sources "$root/Assets/Unity/MobileLink/MailProtocol.cs" "$root/Assets/Unity/MobileLink/AccountMailLink.cs" "$PSScriptRoot/MailVerification.cs"
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
& "$EditorData/MonoBleedingEdge/bin/mono.exe" "$out/tests.exe" "$root/Assets/StreamingAssets/Data"
if($LASTEXITCODE -ne 0){throw 'Mobile mail checks failed'}
