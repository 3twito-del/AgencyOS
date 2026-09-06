$ErrorActionPreference = "Stop"

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$logDir = Join-Path $root ".agencyos\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$inputJson = [Console]::In.ReadToEnd()
$timestamp = [DateTimeOffset]::UtcNow.ToString("o")
$entry = [ordered]@{
    timestampUtc = $timestamp
    event = "ClaudeConfigChange"
    payload = $inputJson
}
($entry | ConvertTo-Json -Compress -Depth 10) | Add-Content -Encoding UTF8 (Join-Path $logDir "claude-config-change.jsonl")
exit 0
