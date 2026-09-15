param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 New-Item -ItemType Directory -Force "$root/Library/OrientationCompile" | Out-Null
 foreach($name in @('AfterSeoul.Game','AfterSeoul.Unity','AfterSeoul.Editor')){
  $lines=Get-Content "$root/Library/Bee/artifacts/1300b0aE.dag/$name.rsp" | Where-Object {$_ -notmatch '^(-out:|-refout:|-analyzer:|/additionalfile:)' -and $_ -notmatch '^"Assets/'}
  foreach($dependency in @('AfterSeoul.Game','AfterSeoul.Unity')){
   $lines=$lines.Replace("Library/Bee/artifacts/1300b0aE.dag/$dependency.ref.dll","Library/OrientationCompile/$dependency.dll")
  }
  $folder=if($name -eq 'AfterSeoul.Game'){'Assets/Game'}elseif($name -eq 'AfterSeoul.Unity'){'Assets/Unity'}else{'Assets/Unity/Editor'}
  $sources=Get-ChildItem $folder -Filter *.cs -Recurse | Where-Object {$name -ne 'AfterSeoul.Unity' -or $_.FullName -notmatch '[\/]Editor[\/]'}
  $lines=@($lines)+@("-out:Library/OrientationCompile/$name.dll")+@($sources | ForEach-Object {'"'+$_.FullName+'"'})
  $rsp="$root/Library/OrientationCompile/$name.rsp"
  [IO.File]::WriteAllLines($rsp,$lines)
  & "$EditorData/MonoBleedingEdge/bin/mono.exe" "$EditorData/MonoBleedingEdge/lib/mono/4.5/csc.exe" "@$rsp"
  if($LASTEXITCODE -ne 0){throw "Compile failed: $name"}
 }
 'PASS: current game, UI and editor sources compiled.'
} finally {Pop-Location}
