@echo off
setlocal

rem Releases: https://github.com/rust-lang/mdBook/releases
set "MDBOOK_VERSION=0.5.2"
set "MDBOOK_ARCHIVE_NAME=mdbook-v%MDBOOK_VERSION%-x86_64-pc-windows-msvc.zip"
set "MDBOOK_ARCHIVE_SHA256=E78FA1159BFC381D03F9C6659C48C883706497DC63C9153007A8A4C8DF8DA166"
set "MDBOOK_DOWNLOAD_URL=https://github.com/rust-lang/mdBook/releases/download/v%MDBOOK_VERSION%/%MDBOOK_ARCHIVE_NAME%"
set "MDBOOK_DESTINATION_ONE=%~dp0mdbook.exe"
set "MDBOOK_DESTINATION_TWO=%~dp0..\VolumeTrayAppDotNET\documentation\mdbook.exe"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference = 'Stop';" ^
    "$exitCode = 0;" ^
    "$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('TrayAppDotNET-mdBook-' + [System.Guid]::NewGuid().ToString('N'));" ^
    "try {" ^
    "    [void](New-Item -ItemType Directory -Path $temporaryDirectory);" ^
    "    $archivePath = Join-Path $temporaryDirectory $env:MDBOOK_ARCHIVE_NAME;" ^
    "    Invoke-WebRequest -UseBasicParsing -Uri $env:MDBOOK_DOWNLOAD_URL -OutFile $archivePath;" ^
    "    $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create();" ^
    "    $archiveStream = [System.IO.File]::OpenRead($archivePath);" ^
    "    try { $hashBytes = $hashAlgorithm.ComputeHash($archiveStream); } finally { $archiveStream.Dispose(); $hashAlgorithm.Dispose(); }" ^
    "    $actualHash = [System.BitConverter]::ToString($hashBytes).Replace('-', '');" ^
    "    if ($actualHash -ne $env:MDBOOK_ARCHIVE_SHA256) { throw ('mdBook archive SHA-256 mismatch: ' + $actualHash); }" ^
    "    Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
    "    $extractDirectory = Join-Path $temporaryDirectory 'extracted';" ^
    "    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractDirectory);" ^
    "    $executablePath = Join-Path $extractDirectory 'mdbook.exe';" ^
    "    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw 'The mdBook archive does not contain mdbook.exe'; }" ^
    "    [System.IO.File]::Copy($executablePath, $env:MDBOOK_DESTINATION_ONE, $true);" ^
    "    [System.IO.File]::Copy($executablePath, $env:MDBOOK_DESTINATION_TWO, $true);" ^
    "} catch {" ^
    "    [System.Console]::Error.WriteLine($_.Exception.Message);" ^
    "    $exitCode = 1;" ^
    "} finally {" ^
    "    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force; }" ^
    "}" ^
    "exit $exitCode;"

if errorlevel 1 exit /b 1

echo Installed mdBook %MDBOOK_VERSION% in both documentation directories.
