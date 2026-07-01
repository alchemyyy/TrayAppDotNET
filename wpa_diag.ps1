$ErrorActionPreference = "Stop"

$appPath = Join-Path $PSScriptRoot "bin\Release\VolumeTrayAppDOTNET.exe"
$outPath = Join-Path $PSScriptRoot "VolumeTrayAppDOTNET-heap.etl"

wpr -snapshotconfig heap -name VolumeTrayAppDOTNET.exe enable
wpr -start heapsnapshot -filemode

$env:TrayAppDotNET_NO_WATCHER = "1"
$proc = Start-Process -FilePath $appPath -ArgumentList "--monitored" -PassThru

Read-Host "Press Enter to take heap snapshot"

wpr -singlesnapshot heap $proc.Id
wpr -stop $outPath
wpr -snapshotconfig heap -name VolumeTrayAppDOTNET.exe disable

Write-Host "Saved: $outPath"
