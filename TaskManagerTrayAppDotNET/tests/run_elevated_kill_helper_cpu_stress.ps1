param(
    [ValidateRange(1, 256)]
    [int]$WorkerCount = [Environment]::ProcessorCount
)

$ErrorActionPreference = 'Stop'
$testProject = Join-Path $PSScriptRoot 'TaskManagerTrayAppDotNET.Tests\TaskManagerTrayAppDotNET.Tests.csproj'
$workers = [Collections.Generic.List[Diagnostics.Process]]::new()

dotnet build $testProject -c Debug -p:SkipKillRunningInstance=true
if ($LASTEXITCODE -ne 0) {
    throw "Test build failed with exit code $LASTEXITCODE."
}

try {
    $workerArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        'while ($true) { }'
    )
    for ($workerIndex = 0; $workerIndex -lt $WorkerCount; $workerIndex++) {
        $worker = Start-Process `
            -FilePath 'powershell.exe' `
            -ArgumentList $workerArguments `
            -WindowStyle Hidden `
            -PassThru
        $workers.Add($worker)
    }

    Start-Sleep -Seconds 2
    $env:TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST = '1'
    dotnet test $testProject `
        -c Debug `
        --no-build `
        --no-restore `
        --filter 'FullyQualifiedName~ElevatedKillHelperSmokeTests'
    if ($LASTEXITCODE -ne 0) {
        throw "Elevated helper stress test failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST -ErrorAction SilentlyContinue
    foreach ($worker in $workers) {
        if (!$worker.HasExited) {
            Stop-Process -Id $worker.Id -Force
        }
        $worker.Dispose()
    }
}
