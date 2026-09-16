param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data', [string]$MainlineRoot='D:/singleProject/EscapeFromSeoul')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $out='Library/MailCompile'
 New-Item -ItemType Directory -Force $out | Out-Null
 foreach($name in @('AfterSeoul.Game','AfterSeoul.Unity','AfterSeoul.Editor')){
  $rsp=Get-ChildItem Library/Bee/artifacts -Recurse -Filter "$name.rsp" | Select-Object -First 1
  $lines=Get-Content $rsp.FullName | Where-Object {$_ -notmatch '^(-out:|-refout:|-analyzer:|/additionalfile:)' -and $_ -notmatch '^"Assets/'}
  foreach($dependency in @('AfterSeoul.Game','AfterSeoul.Unity')){
   $lines=$lines -replace ('Library/Bee/artifacts/[^/]+/'+[regex]::Escape($dependency)+'.ref.dll'),"$out/$dependency.dll"
  }
  if($name -eq 'AfterSeoul.Unity'){
   $lines=@($lines)+@(Get-ChildItem "$MainlineRoot/Library/ScriptAssemblies" -Filter 'Unity.Services*.dll' | Where-Object {$_.Name -notmatch 'Editor|Lobb|Relay|QoS|Wire'} | ForEach-Object {'-r:"'+$_.FullName+'"'})
  }
  $folder=if($name -eq 'AfterSeoul.Game'){'Assets/Game'}elseif($name -eq 'AfterSeoul.Unity'){'Assets/Unity'}else{'Assets/Unity/Editor'}
  $sources=Get-ChildItem $folder -Recurse -Filter *.cs | Where-Object {$name -ne 'AfterSeoul.Unity' -or $_.FullName -notmatch '[\\/]Editor[\\/]'}
  $lines=@($lines)+@("-out:$out/$name.dll")+@($sources | ForEach-Object {'"'+$_.FullName+'"'})
  [IO.File]::WriteAllLines((Join-Path $root "$out/$name.rsp"),$lines)
  & dotnet "$EditorData/DotNetSdkRoslyn/csc.dll" "@$out/$name.rsp"
  if($LASTEXITCODE -ne 0){throw "Compilation failed: $name"}
 }
 'PASS: mobile game, UI, editor sources compile with Authentication 3.7.4 references from mainline.'
}finally{Pop-Location}
