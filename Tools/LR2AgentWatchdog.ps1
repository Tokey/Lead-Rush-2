# LR2 agent watchdog: keeps at least one AI agent working on LeadRush 2, even when every agent is idle or out of tokens.
#
# Runs outside all agents (Windows Task Scheduler, every 20 min), so it costs no AI tokens itself. It reads
# AGENT_COORDINATION.md, finds each agent's latest sign of life (its section 1 row "Updated" time or its newest signed log
# entry), and prompts any agent that has been silent for longer than -StaleMinutes through that agent's own CLI:
#   Gemini  (Antigravity)  agy --continue --mode auto --dangerously-skip-permissions -p <prompt>   (falls back to a new gemini-4-hydrogen-high session)
#   Copilot (VS Code)      code chat -m agent -r -a AGENT_COORDINATION.md <prompt>
#   Claude  (Claude Code)  no CLI; Claude's own scheduled tick wakes it, so the watchdog only logs it
# It never starts Unity, never edits code, and skips Gemini while a LeadRush Unity process is running (Gemini is usually busy then).
#
# Try it:     powershell -NoProfile -ExecutionPolicy Bypass -File Tools\LR2AgentWatchdog.ps1 -DryRun
# Register:   see Docs/AI_Collaboration/CONTINUITY.md (the developer decides; it is a persistent scheduled task)
# Log:        Logs/LR2_Watchdog.log

param(
    [int]$StaleMinutes = 25,
    [switch]$DryRun
)

$ErrorActionPreference = 'Continue'
$proj = Split-Path -Parent $PSScriptRoot
$board = Join-Path $proj 'AGENT_COORDINATION.md'
$logDir = Join-Path $proj 'Logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$log = Join-Path $logDir 'LR2_Watchdog.log'
function Write-WatchdogLog([string]$m) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $m" | Add-Content -Path $log -Encoding utf8 }

if (-not (Test-Path $board)) { Write-WatchdogLog "board not found: $board"; exit 1 }
$text = Get-Content $board -Raw -Encoding utf8
$now = Get-Date

function ConvertTo-LastSeen([string]$hhmm) {
    $t = [datetime]::ParseExact($hhmm, 'HH:mm', $null)
    $t = $now.Date.AddHours($t.Hour).AddMinutes($t.Minute)
    if ($t -gt $now.AddMinutes(5)) { $t = $t.AddDays(-1) }  # written before midnight
    return $t
}

$lastSeen = @{}
foreach ($agent in 'Claude', 'Gemini', 'Copilot') {
    $times = @()
    $row = [regex]::Match($text, "(?m)^\| $agent \|.*\| (\d{2}:\d{2}) \|\s*$")
    if ($row.Success) { $times += (ConvertTo-LastSeen $row.Groups[1].Value) }
    foreach ($m in [regex]::Matches($text, "(?m)^- \[$agent[^\]]*?(\d{2}:\d{2})\]")) { $times += (ConvertTo-LastSeen $m.Groups[1].Value) }
    if ($times.Count -gt 0) { $lastSeen[$agent] = ($times | Sort-Object | Select-Object -Last 1) }
}

$unityBusy = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'Unity.exe' -and $_.CommandLine -match 'LeadRush|projectPath \.' -and $_.CommandLine -notmatch 'AssetImportWorker'
}).Count -gt 0

$prompt = 'Watchdog (automatic): you have been quiet on AGENT_COORDINATION.md for {0} min. Re-read sections 0-3, then continue with your ' +
          'highest-lane rows (bugs, then graphics, then settings/menu/UI), following protocol v3. If you are low on tokens, hand ' +
          'your rows off on the board first. Sign entries with your tool name, model and the real clock time.'

foreach ($agent in 'Gemini', 'Copilot', 'Claude') {
    if (-not $lastSeen.ContainsKey($agent)) { Write-WatchdogLog "$agent no sign of life found on the board"; continue }
    $age = [int]($now - $lastSeen[$agent]).TotalMinutes
    if ($age -lt $StaleMinutes) { Write-WatchdogLog "$agent ok (last seen $age min ago)"; continue }
    $p = $prompt -f $age
    switch ($agent) {
        'Gemini' {
            if ($unityBusy) { Write-WatchdogLog "Gemini quiet $age min but Unity is running; skipped"; break }
            $agy = Join-Path $env:USERPROFILE '.gemini\bin\agy.exe'
            if ($DryRun) { Write-WatchdogLog "DRY RUN would prompt Gemini (quiet $age min)"; break }
            Start-Process -FilePath $agy -ArgumentList @('--continue', '--mode', 'auto', '--dangerously-skip-permissions', '-p', $p) -WorkingDirectory $proj -WindowStyle Minimized
            Write-WatchdogLog "prompted Gemini (quiet $age min)"
        }
        'Copilot' {
            if ($DryRun) { Write-WatchdogLog "DRY RUN would prompt Copilot (quiet $age min)"; break }
            Start-Process -FilePath 'code' -ArgumentList @('chat', '-m', 'agent', '-r', '-a', $board, $p) -WorkingDirectory $proj -WindowStyle Hidden
            Write-WatchdogLog "prompted Copilot (quiet $age min)"
        }
        'Claude' { Write-WatchdogLog "Claude quiet $age min (no CLI; its scheduled tick resumes it after a usage-limit reset)" }
    }
}
