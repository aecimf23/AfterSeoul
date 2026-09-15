param(
    [Parameter(Mandatory = $true)][string]$EditorData,
    [int]$Samples = 3000,
    [string]$OutputPath = 'Library/Economy/current.csv'
)
$ErrorActionPreference = 'Stop'
if ($Samples -lt 1) { throw 'Samples must be positive.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $buildDirectory = Join-Path $projectRoot 'Library/Economy'
    New-Item -ItemType Directory -Force $buildDirectory | Out-Null
    $jsonDll = (Get-ChildItem 'Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll' | Select-Object -First 1).FullName
    if (!$jsonDll) { throw 'Restore Unity packages before running this simulation.' }
    Copy-Item -LiteralPath $jsonDll -Destination $buildDirectory -Force
    $mono = Join-Path $EditorData 'MonoBleedingEdge/bin/mono.exe'
    $compiler = Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
    $netstandard = Join-Path $EditorData 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
    $executable = Join-Path $buildDirectory 'current.exe'
    $sources = @(Get-ChildItem Assets/Game -Filter *.cs -Recurse | ForEach-Object FullName)
    & $mono $compiler /nologo /warn:0 "/out:$executable" "/r:$jsonDll" "/r:$netstandard" @sources Tools/EconomySimulation.cs
    if ($LASTEXITCODE -ne 0) { throw 'Economy harness compilation failed.' }
    & $mono $executable Assets/StreamingAssets/Data $Samples | Set-Content -Encoding utf8 $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'Economy simulation failed.' }
    Write-Output "Economy matrix written to $OutputPath ($Samples seeds per configuration)."
}
finally { Pop-Location }
