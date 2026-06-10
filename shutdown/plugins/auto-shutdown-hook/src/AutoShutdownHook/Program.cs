using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var command = args.FirstOrDefault();
var dryRun = args.Any(static arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));

if (string.Equals(command, "hook", StringComparison.OrdinalIgnoreCase))
{
    return RunHook(dryRun);
}

Console.WriteLine("auto-shutdown-hook");
Console.WriteLine("Usage:");
Console.WriteLine("  auto-shutdown-hook.exe hook");
Console.WriteLine("  auto-shutdown-hook.exe hook --dry-run");
Console.WriteLine();
Console.WriteLine("Handled prompt commands:");
Console.WriteLine("  \u5173\u673a");
Console.WriteLine("  \u5173\u673a <seconds>");
Console.WriteLine("  \u53d6\u6d88\u5173\u673a");
return 0;

static int RunHook(bool dryRun)
{
    try
    {
        var stdin = Console.In.ReadToEnd();
        if (string.IsNullOrWhiteSpace(stdin))
        {
            WriteHookJson(new HookResponse { Continue = true });
            return 0;
        }

        using var document = JsonDocument.Parse(stdin);
        var root = document.RootElement;
        var eventName = GetString(root, "hook_event_name");
        if (!string.Equals(eventName, "UserPromptSubmit", StringComparison.Ordinal))
        {
            WriteHookJson(new HookResponse { Continue = true });
            return 0;
        }

        var prompt = GetString(root, "prompt") ?? "";
        var parsed = ParsePrompt(prompt);
        if (parsed is null)
        {
            WriteHookJson(new HookResponse { Continue = true });
            return 0;
        }

        if (parsed.Action == ShutdownAction.Schedule)
        {
            if (parsed.DelaySeconds is < 0 or > 315360000)
            {
                WriteHookJson(HookResponse.Block($"Invalid shutdown delay: {parsed.DelaySeconds}"));
                return 0;
            }

            var result = RunShutdown(["/s", "/t", parsed.DelaySeconds.ToString()], dryRun);
            WriteHookJson(result.ExitCode == 0
                ? HookResponse.Block($"Shutdown scheduled in {parsed.DelaySeconds} seconds. Send the cancel-shutdown command to abort it.")
                : HookResponse.Block($"Shutdown scheduling failed with exit code {result.ExitCode}: {result.Output}"));
            return 0;
        }

        if (parsed.Action == ShutdownAction.Cancel)
        {
            var result = RunShutdown(["/a"], dryRun);
            WriteHookJson(result.ExitCode == 0
                ? HookResponse.Block("Pending shutdown canceled.")
                : HookResponse.Block($"Shutdown cancellation returned exit code {result.ExitCode}: {result.Output}"));
            return 0;
        }

        WriteHookJson(new HookResponse { Continue = true });
        return 0;
    }
    catch (Exception ex)
    {
        WriteHookJson(HookResponse.Block($"Auto shutdown hook failed: {ex.Message}"));
        return 0;
    }
}

static ParsedCommand? ParsePrompt(string prompt)
{
    const string shutdownWord = "\u5173\u673a";
    const string cancelWord = "\u53d6\u6d88\u5173\u673a";

    var singleLine = Regex.Replace(prompt.Replace("\r\n", "\n").Trim(), @"\s+", " ");
    if (string.Equals(singleLine, shutdownWord, StringComparison.Ordinal))
    {
        return new ParsedCommand(ShutdownAction.Schedule, 60);
    }

    var scheduleMatch = Regex.Match(singleLine, $"^{Regex.Escape(shutdownWord)}\\s+(\\d{{1,9}})$");
    if (scheduleMatch.Success && int.TryParse(scheduleMatch.Groups[1].Value, out var delaySeconds))
    {
        return new ParsedCommand(ShutdownAction.Schedule, delaySeconds);
    }

    if (string.Equals(singleLine, cancelWord, StringComparison.Ordinal))
    {
        return new ParsedCommand(ShutdownAction.Cancel, 0);
    }

    return null;
}

static ShutdownResult RunShutdown(string[] arguments, bool dryRun)
{
    if (dryRun)
    {
        return new ShutdownResult(0, $"dry run: shutdown.exe {string.Join(' ', arguments)}");
    }

    var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    var shutdownExe = Path.Combine(systemRoot, "System32", "shutdown.exe");
    var startInfo = new ProcessStartInfo
    {
        FileName = shutdownExe,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start shutdown.exe.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    var output = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(static s => s.Length > 0));
    return new ShutdownResult(process.ExitCode, output);
}

static string? GetString(JsonElement root, string propertyName)
{
    return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;
}

static void WriteHookJson(HookResponse response)
{
    var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
    Console.WriteLine(json);
}

internal enum ShutdownAction
{
    Schedule,
    Cancel,
}

internal sealed record ParsedCommand(ShutdownAction Action, int DelaySeconds);

internal sealed record ShutdownResult(int ExitCode, string Output);

internal sealed class HookResponse
{
    [JsonPropertyName("continue")]
    public bool? Continue { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    public static HookResponse Block(string reason)
    {
        return new HookResponse { Decision = "block", Reason = reason };
    }
}
