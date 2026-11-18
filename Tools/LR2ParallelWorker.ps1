<#
.SYNOPSIS
Creates and runs an isolated parallel Unity worker directory (NTFS junctions to Assets/Packages/ProjectSettings)
so builds, pose checks, or QA runs execute concurrently with the developer's open Unity Editor without hitting
Temp/UnityLockfile collisions.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File Tools\LR2ParallelWorker.ps1 -ExecuteMethod LR2.EditorTools.LR2BuildPipeline.BuildAllAndPlayerBatch

.EXAMPLE
powershell -ExecutionPolicy Bypass -File Tools\LR2ParallelWorker.ps1 -Visible -ExecuteMethod LR2.EditorTools.LR2SmokeTest.RunBatch -ExtraArgs "-lr2PoseCheck -lr2Weapon mk18 -lr2Scene Assets\LR2\Scenes\LR2_TestRange.unity -lr2OutDir Logs\LR2_PoseCheck_Mk18 -lr2Duration 52"
#>
param(
    [string]$WorkerRoot = "H:\Unity Projects\Builds\LR2_ParallelWorker",
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe",
    [string]$ExecuteMethod = "LR2.EditorTools.LR2BuildPipeline.BuildAllAndPlayerBatch",
    [string]$ExtraArgs = "",
    [string]$LogFile = "",
    [switch]$Visible
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path $RepoRoot "Logs\parallel_worker.log"
}
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null
New-Item -ItemType Directory -Force -Path $WorkerRoot | Out-Null

foreach ($dir in @("Assets", "Packages", "ProjectSettings")) {
    $link = Join-Path $WorkerRoot $dir
    $target = Join-Path $RepoRoot $dir
    if (-not (Test-Path $link)) {
        cmd /c mklink /J "`"$link`"" "`"$target`"" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create NTFS junction $link -> $target"
        }
        Write-Host "[LR2ParallelWorker] Junction created: $link -> $target"
    }
}

$workerLib = Join-Path $WorkerRoot "Library"
$repoLib = Join-Path $RepoRoot "Library"
if (-not (Test-Path $workerLib) -and (Test-Path $repoLib)) {
    Write-Host "[LR2ParallelWorker] Seeding Library cache from $repoLib (skipping any locked DB files)..."
    robocopy $repoLib $workerLib /E /R:0 /W:0 /NFL /NDL /NJH /NJS /NP /XD Temp Artifacts il2cpp_cache | Out-Null
    Write-Host "[LR2ParallelWorker] Library seed finished (robocopy code=$LASTEXITCODE)."
}

New-Item -ItemType Directory -Force -Path (Join-Path $WorkerRoot "Temp") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $WorkerRoot "Logs") | Out-Null

$argList = @()
if ($Visible) {
    $argList += "-screen-fullscreen 0 -screen-width 1120 -screen-height 630"
} else {
    $argList += "-batchmode -nographics"
}
$argList += "-projectPath `"$WorkerRoot`""
if (-not [string]::IsNullOrWhiteSpace($ExecuteMethod)) {
    $argList += "-executeMethod $ExecuteMethod"
}
if (-not [string]::IsNullOrWhiteSpace($ExtraArgs)) {
    $argList += $ExtraArgs
}
$argList += "-logFile `"$LogFile`""
if (-not $Visible) {
    $argList += "-quit"
}

$argStr = $argList -join " "
Write-Host "[LR2ParallelWorker] Launching: $UnityExe $argStr"
$proc = Start-Process -FilePath $UnityExe -ArgumentList $argStr -PassThru
$proc.WaitForExit()
Write-Host "[LR2ParallelWorker] Unity exited code=$($proc.ExitCode). Log: $LogFile"
exit $proc.ExitCode
