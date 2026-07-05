@echo off
setlocal

set "CLEANOUT_ROOT=%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$root = (Resolve-Path -LiteralPath $env:CLEANOUT_ROOT).Path.TrimEnd('\');" ^
  "$targetNames = @('.vs', 'bin', 'obj');" ^
  "$sourceExtensions = @('.csproj', '.fsproj', '.vbproj', '.vcxproj', '.sln', '.slnx', '.props', '.targets', '.fs', '.vb', '.cpp', '.c', '.h', '.hpp', '.axaml', '.xaml');" ^
  "$sourceNames = @('CMakeLists.txt', 'package.json', 'Directory.Build.props', 'Directory.Build.targets');" ^
  "$generatedCSharpPatterns = @('*.g.cs', '*.g.i.cs', '*AssemblyInfo.cs', '*AssemblyAttributes.cs', 'GlobalUsings.g.cs', 'TemporaryGeneratedFile_*.cs');" ^
  "Write-Host ('Cleaning build folders under: ' + $root);" ^
  "$directories = Get-ChildItem -LiteralPath $root -Directory -Recurse -Force | Where-Object { $targetNames -contains $_.Name } | Sort-Object FullName -Descending;" ^
  "$deletedCount = 0;" ^
  "$skippedCount = 0;" ^
  "foreach ($directory in $directories) {" ^
  "  $resolvedPath = $directory.FullName;" ^
  "  if (-not $resolvedPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { Write-Warning ('Skipping outside root: ' + $resolvedPath); $skippedCount++; continue; }" ^
  "  $sourceFile = Get-ChildItem -LiteralPath $resolvedPath -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { if (($sourceExtensions -contains $_.Extension.ToLowerInvariant()) -or ($sourceNames -contains $_.Name)) { return $true }; if ($_.Extension.ToLowerInvariant() -ne '.cs') { return $false }; foreach ($pattern in $generatedCSharpPatterns) { if ($_.Name -like $pattern) { return $false } }; return $true } | Select-Object -First 1;" ^
  "  if ($null -ne $sourceFile) { Write-Warning ('Skipping possible source folder: ' + $resolvedPath + ' (found ' + $sourceFile.Name + ')'); $skippedCount++; continue; }" ^
  "  Write-Host ('Deleting: ' + $resolvedPath);" ^
  "  Remove-Item -LiteralPath $resolvedPath -Recurse -Force;" ^
  "  $deletedCount++;" ^
  "}" ^
  "Write-Host ('Deleted ' + $deletedCount + ' folder(s); skipped ' + $skippedCount + ' possible source folder(s).');"

if errorlevel 1 exit /b %errorlevel%
exit /b 0
