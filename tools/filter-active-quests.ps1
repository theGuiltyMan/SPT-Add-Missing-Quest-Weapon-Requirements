param(
    [string]$InputFile = "report.json",
    [string]$OutputFile = "report_active.json"
)

$data = Get-Content $InputFile -Raw | ConvertFrom-Json
$data.Quests = $data.Quests | Where-Object { $_.Noop -eq $false }
$data | ConvertTo-Json -Depth 100 | Set-Content $OutputFile -Encoding UTF8

Write-Host "Done: $($data.Quests.Count) active quests written to $OutputFile"
