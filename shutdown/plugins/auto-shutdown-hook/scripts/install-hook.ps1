param(
    [string]$CodexHome = $env:CODEX_HOME
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path $env:USERPROFILE ".codex"
}

$hooksPath = Join-Path $CodexHome "hooks.json"
$pluginRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$hookScript = Join-Path $pluginRoot "scripts\auto-shutdown-hook.ps1"
$hookExe = Join-Path $pluginRoot "artifacts\win-x64\auto-shutdown-hook.exe"
if (Test-Path -LiteralPath $hookExe) {
    if ($hookExe -match "\s") {
        $hookCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& ''{0}'' hook"' -f $hookExe
    } else {
        $hookCommand = '{0} hook' -f $hookExe
    }
} else {
    $hookCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $hookScript
}

function Read-HooksConfig {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ hooks = [pscustomobject]@{} }
    }

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [pscustomobject]@{ hooks = [pscustomobject]@{} }
    }

    $config = $raw | ConvertFrom-Json
    if ($null -eq $config.PSObject.Properties["hooks"]) {
        $config | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
    }
    return $config
}

function Set-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [object]$Value
    )

    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    } else {
        $Object.$Name = $Value
    }
}

function Is-AutoShutdownHook {
    param([object]$Hook)

    $command = [string]$Hook.command
    return $command.IndexOf("auto-shutdown-hook", [StringComparison]::OrdinalIgnoreCase) -ge 0
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $hooksPath) | Out-Null

if (Test-Path -LiteralPath $hooksPath) {
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    Copy-Item -LiteralPath $hooksPath -Destination "$hooksPath.bak-auto-shutdown-$timestamp" -Force
}

$config = Read-HooksConfig -Path $hooksPath
$hooks = $config.hooks
$existingEntries = @()
if ($null -ne $hooks.PSObject.Properties["UserPromptSubmit"]) {
    $existingEntries = @($hooks.UserPromptSubmit)
}

$keptEntries = New-Object System.Collections.Generic.List[object]
foreach ($entry in $existingEntries) {
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

$newEntry = [pscustomobject]@{
    hooks = @(
        [pscustomobject]@{
            type = "command"
            command = $hookCommand
            timeout = 5
            statusMessage = "Checking auto-shutdown command"
        }
    )
}

$allEntries = @($keptEntries.ToArray()) + @($newEntry)
Set-ObjectProperty -Object $hooks -Name "UserPromptSubmit" -Value @($allEntries)

$json = $config | ConvertTo-Json -Depth 20
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hooksPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Installed auto-shutdown UserPromptSubmit hook:"
Write-Host $hooksPath
Write-Host "Trust the updated hook in Codex with /hooks if prompted."
