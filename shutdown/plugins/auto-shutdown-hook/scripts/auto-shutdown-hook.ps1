param(
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

try {
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
} catch {
}

function New-TextFromCodePoints {
    param([int[]]$CodePoints)

    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

function Write-HookJson {
    param([object]$Value)

    $json = $Value | ConvertTo-Json -Compress -Depth 8
    [Console]::Out.WriteLine($json)
}

function Continue-Hook {
    Write-HookJson @{ continue = $true }
}

function Block-Hook {
    param([string]$Reason)

    Write-HookJson @{
        decision = "block"
        reason = $Reason
    }
}

function Invoke-Shutdown {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    if ($DryRun) {
        return @{
            ExitCode = 0
            Output = "dry run: shutdown.exe $($Arguments -join ' ')"
        }
    }

    $shutdownExe = Join-Path $env:SystemRoot "System32\shutdown.exe"
    $output = & $shutdownExe @Arguments 2>&1
    return @{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Get-PromptCommand {
    param([string]$Prompt)

    $shutdownWord = New-TextFromCodePoints @(0x5173, 0x673A)
    $cancelWord = New-TextFromCodePoints @(0x53D6, 0x6D88, 0x5173, 0x673A)

    $normalized = ($Prompt -replace "`r`n", "`n").Trim()
    $singleLine = ($normalized -replace "\s+", " ").Trim()

    if ($singleLine -eq $shutdownWord) {
        return @{ Action = "on"; DelaySeconds = 60 }
    }

    $shutdownPattern = "^{0}\s+(\d{{1,9}})$" -f [regex]::Escape($shutdownWord)
    if ($singleLine -match $shutdownPattern) {
        return @{ Action = "on"; DelaySeconds = [int]$Matches[1] }
    }

    if ($singleLine -eq $cancelWord) {
        return @{ Action = "off"; DelaySeconds = 0 }
    }

    return $null
}

try {
    $stdin = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($stdin)) {
        Continue-Hook
        exit 0
    }

    $payload = $stdin | ConvertFrom-Json
    if ($payload.hook_event_name -ne "UserPromptSubmit") {
        Continue-Hook
        exit 0
    }

    $prompt = [string]$payload.prompt
    $command = Get-PromptCommand -Prompt $prompt
    if ($null -eq $command) {
        Continue-Hook
        exit 0
    }

    if ($command.Action -eq "on") {
        $delay = [int]$command.DelaySeconds
        if ($delay -lt 0 -or $delay -gt 315360000) {
            Block-Hook "Invalid shutdown delay: $delay"
            exit 0
        }

        $result = Invoke-Shutdown -Arguments @("/s", "/t", $delay.ToString())
        if ($result.ExitCode -eq 0) {
            Block-Hook "Shutdown scheduled in $delay seconds. Send the cancel-shutdown command to abort it."
        } else {
            Block-Hook "Shutdown scheduling failed with exit code $($result.ExitCode): $($result.Output)"
        }
        exit 0
    }

    if ($command.Action -eq "off") {
        $result = Invoke-Shutdown -Arguments @("/a")
        if ($result.ExitCode -eq 0) {
            Block-Hook "Pending shutdown canceled."
        } else {
            Block-Hook "Shutdown cancellation returned exit code $($result.ExitCode): $($result.Output)"
        }
        exit 0
    }

    Continue-Hook
    exit 0
} catch {
    Block-Hook "Auto shutdown hook failed: $($_.Exception.Message)"
    exit 0
}
