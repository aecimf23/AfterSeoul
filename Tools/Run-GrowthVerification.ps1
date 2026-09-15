param(
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data',
    [string]$OutputPath = 'Logs/onboarding-20260915'
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$buildDirectory=Join-Path $projectRoot 'Library/Onboarding'
New-Item -ItemType Directory -Force $buildDirectory | Out-Null
$jsonDll=(Get-ChildItem "$projectRoot/Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll" | Select-Object -First 1).FullName
if (!$jsonDll) { throw 'Restore Unity packages before running.' }
Copy-Item -LiteralPath $jsonDll -Destination $buildDirectory -Force
$mono=Join-Path $EditorData 'MonoBleedingEdge/bin/mono.exe'
$compiler=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
$netstandard=Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
$sources=@(Get-ChildItem "$projectRoot/Assets/Game" -Filter *.cs -Recurse | ForEach-Object FullName)
$executable=Join-Path $buildDirectory 'growth.exe'
& $mono $compiler /nologo /warn:0 "/out:$executable" "/r:$jsonDll" "/r:$netstandard" @sources "$PSScriptRoot/GrowthVerification.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
$output=if([IO.Path]::IsPathRooted($OutputPath)){$OutputPath}else{Join-Path $projectRoot $OutputPath}
$run=Start-Process -FilePath $mono -ArgumentList @("`"$executable`"", "`"$projectRoot/Assets/StreamingAssets/Data`"", "`"$output`"") -WindowStyle Hidden -RedirectStandardOutput "$buildDirectory/stdout.log" -RedirectStandardError "$buildDirectory/stderr.log" -PassThru
$run.PriorityClass='BelowNormal'
$run.WaitForExit()
if($run.ExitCode -ne 0) { Get-Content "$buildDirectory/stderr.log"; throw 'Simulation failed' }
Get-Content "$buildDirectory/stdout.log"
