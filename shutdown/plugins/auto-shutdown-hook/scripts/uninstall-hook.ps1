param(
    [string]$CodexHome = $env:CODEX_HOME
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path $env:USERPROFILE ".codex"
}

$hooksPath = Join-Path $CodexHome "hooks.json"
if (-not (Test-Path -LiteralPath $hooksPath)) {
    Write-Host "hooks.json not found:"
    Write-Host $hooksPath
    exit 0
}

function Is-AutoShutdownHook {
    param([object]$Hook)

    $command = [string]$Hook.command
    return $command.IndexOf("auto-shutdown-hook", [StringComparison]::OrdinalIgnoreCase) -ge 0
}

$timestamp = Get-Date -Format "yyyyMMddHHmmss"
Copy-Item -LiteralPath $hooksPath -Destination "$hooksPath.bak-auto-shutdown-uninstall-$timestamp" -Force

$config = (Get-Content -LiteralPath $hooksPath -Raw -Encoding UTF8) | ConvertFrom-Json
if ($null -eq $config.PSObject.Properties["hooks"] -or
    $null -eq $config.hooks.PSObject.Properties["UserPromptSubmit"]) {
    Write-Host "Auto-shutdown hook not installed."
    exit 0
}

$keptEntries = New-Object System.Collections.Generic.List[object]
foreach ($entry in @($config.hooks.UserPromptSubmit)) {
    if ($null -eq $entry.PSObject.Properties["hooks"]) {
        $keptEntries.Add($entry)
        continue
    }

    $keptHookCommands = @($entry.hooks | Where-Object { -not (Is-AutoShutdownHook $_) })
    if ($keptHookCommands.Count -gt 0) {
        $entry.hooks = @($keptHookCommands)
        $keptEntries.Add($entry)
    }
}

if ($keptEntries.Count -eq 0) {
    $config.hooks.PSObject.Properties.Remove("UserPromptSubmit")
} else {
    $config.hooks.UserPromptSubmit = @($keptEntries.ToArray())
}

$json = $config | ConvertTo-Json -Depth 20
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hooksPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Uninstalled auto-shutdown UserPromptSubmit hook:"
Write-Host $hooksPath
