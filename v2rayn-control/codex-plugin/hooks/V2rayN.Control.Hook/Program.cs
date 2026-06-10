using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Program
{
    private const string DefaultPipeName = "v2rayn-control";
    private const string PendingNode = "node";
    private const string PendingGroup = "group";
    private const string UngroupedKey = "__ungrouped__";
    private const string UngroupedName = "(ungrouped)";
    private static readonly TimeSpan TestCallbackWait = TimeSpan.FromSeconds(25);

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private sealed class PluginState
    {
        public string? CurrentGroupKey { get; set; }
        public string? CurrentGroupName { get; set; }
        public string? PendingSelection { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed record NodeSnapshot(JsonArray Nodes, string CurrentIndexId);

    private sealed record GroupView(int Number, string Key, string Name, int Count, bool Current, bool ContainsCurrentNode);

    private sealed record NodeView(
        int Number,
        JsonObject Node,
        string IndexId,
        string Name,
        string GroupKey,
        string GroupName,
        bool Current);

    private sealed record TestSummary(bool Started, string Message, Dictionary<string, JsonObject> Updates);

    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);

        if (args.Length == 0 || !string.Equals(args[0], "hook", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: V2rayN.Control.Hook.exe hook");
            return 2;
        }

        return await RunHook();
    }

    private static async Task<int> RunHook()
    {
        try
        {
            var stdin = await Console.In.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(stdin))
            {
                await WriteHookJson(new { @continue = true });
                return 0;
            }

            using var document = JsonDocument.Parse(stdin);
            var root = document.RootElement;
            var eventName = GetString(root, "hook_event_name");
            if (!string.Equals(eventName, "UserPromptSubmit", StringComparison.Ordinal))
            {
                await WriteHookJson(new { @continue = true });
                return 0;
            }

            var prompt = ExtractEffectivePrompt(GetString(root, "prompt") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ClearPendingSelection();
                await WriteHookJson(new { @continue = true });
                return 0;
            }

            if (!ShouldHandleHookPrompt(prompt))
            {
                ClearPendingSelection();
                await WriteHookJson(new { @continue = true });
                return 0;
            }

            var result = await HandleVpnCommand(prompt);
            await WriteHookJson(new
            {
                decision = "block",
                reason = ExtractTextResult(result)
            });
            return 0;
        }
        catch (Exception ex)
        {
            await WriteHookJson(new
            {
                decision = "block",
                reason = "v2rayN VPN hook failed:\n\n" + ex.Message
            });
            return 0;
        }
    }

    private static async Task<Dictionary<string, object?>> HandleVpnCommand(string command)
    {
        var tokens = SplitCommand(command ?? string.Empty);
        if (tokens.Count > 0 && string.Equals(tokens[0], "vpn", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0)
        {
            return TextResult(HelpText());
        }

        if (tokens.Count == 1 && int.TryParse(tokens[0], out var selectedNumber))
        {
            return await HandleNumberSelection(selectedNumber);
        }

        var action = tokens[0].ToLowerInvariant();
        return action switch
        {
            "help" or "-h" or "--help" => TextResult(HelpText()),
            "ping" => TextResult(ToPrettyJson(await CallPipe("ping", new Dictionary<string, object?>()))),
            "list" or "ls" or "show" => await ListCurrentGroup(),
            "auto" => await AutoSwitch(tokens),
            "group" or "groups" => await ListGroups(),
            "reload" or "reload-dll" or "reload-plugin" => await ReloadPlugin(),
            "switch" or "use" or "select" => RemovedCommand("vpn switch has been removed. Run `vpn list`, then enter the node number."),
            "test" or "status" or "stat" or "stop" => RemovedCommand($"vpn {action} has been removed. `vpn list` automatically tests the current group."),
            _ => throw new InvalidOperationException($"Unknown vpn command: {action}")
        };
    }

    private static async Task<Dictionary<string, object?>> ListCurrentGroup()
    {
        var state = LoadState();
        var snapshot = await FetchAllNodes();
        var groups = BuildGroups(snapshot, state.CurrentGroupKey);
        EnsureCurrentGroup(state, groups);

        var nodes = BuildNodeViews(snapshot, state.CurrentGroupKey);
        state.PendingSelection = PendingNode;
        SaveState(state);

        var testSummary = await StartCurrentGroupTest(nodes);
        return TextResult(
            $"Current group: {state.CurrentGroupName}\n" +
            $"Auto test: {testSummary.Message}\n\n" +
            CompactNodeTable(nodes, testSummary.Updates) +
            "\n\nEnter a node number to switch within the current group.");
    }

    private static async Task<Dictionary<string, object?>> ListGroups()
    {
        var state = LoadState();
        var snapshot = await FetchAllNodes();
        var groups = BuildGroups(snapshot, state.CurrentGroupKey);
        EnsureCurrentGroup(state, groups);

        groups = BuildGroups(snapshot, state.CurrentGroupKey);
        state.PendingSelection = PendingGroup;
        SaveState(state);

        return TextResult(
            $"Current group: {state.CurrentGroupName}\n\n" +
            CompactGroupTable(groups) +
            "\n\nEnter a group number to set the plugin current group.");
    }

    private static async Task<Dictionary<string, object?>> AutoSwitch(List<string> tokens)
    {
        if (tokens.Count != 2 || !int.TryParse(tokens[1], out var availableNumber) || availableNumber <= 0)
        {
            return TextResult("Usage: vpn auto N\nN is the 1-based index among usable nodes in the current group after realping.");
        }

        var state = LoadState();
        var snapshot = await FetchAllNodes();
        var groups = BuildGroups(snapshot, state.CurrentGroupKey);
        EnsureCurrentGroup(state, groups);

        var nodes = BuildNodeViews(snapshot, state.CurrentGroupKey);
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException($"Current group `{state.CurrentGroupName}` has no nodes.");
        }

        var testSummary = await StartCurrentGroupTest(nodes);
        var available = nodes
            .Select(node => new
            {
                Node = node,
                Delay = TryGetUsableDelay(node.Node, testSummary.Updates.GetValueOrDefault(node.IndexId), out var delay) ? delay : -1
            })
            .Where(item => item.Delay > 0)
            .ToList();

        if (available.Count == 0)
        {
            state.PendingSelection = null;
            SaveState(state);
            return TextResult(
                $"Current group: {state.CurrentGroupName}\n" +
                $"Auto test: {testSummary.Message}\n\n" +
                "No usable nodes were found.\n\n" +
                CompactNodeTable(nodes, testSummary.Updates));
        }

        if (availableNumber > available.Count)
        {
            state.PendingSelection = null;
            SaveState(state);
            return TextResult(
                $"Current group: {state.CurrentGroupName}\n" +
                $"Auto test: {testSummary.Message}\n\n" +
                $"Only {available.Count} usable nodes found; cannot select usable node {availableNumber}.\n\n" +
                CompactAvailableTable(available.Select((item, index) => (Number: index + 1, item.Node, item.Delay)).ToList()));
        }

        var selected = available[availableNumber - 1];
        var result = await CallPipe("switch", new Dictionary<string, object?>
        {
            ["indexId"] = selected.Node.IndexId
        }, TimeSpan.FromSeconds(10));
        if (!IsOk(result))
        {
            return TextResult(ToPrettyJson(result));
        }

        state.PendingSelection = null;
        SaveState(state);

        return TextResult(
            $"Auto switched to usable node [{availableNumber}] {selected.Node.Name}\n" +
            $"Original no: {selected.Node.Number}\n" +
            $"Group: {state.CurrentGroupName}\n" +
            $"Delay: {selected.Delay} ms\n" +
            $"Auto test: {testSummary.Message}\n\n" +
            CompactAvailableTable(available.Select((item, index) => (Number: index + 1, item.Node, item.Delay)).ToList()) +
            $"\n\n{ToPrettyJson(result)}");
    }

    private static async Task<Dictionary<string, object?>> HandleNumberSelection(int number)
    {
        var state = LoadState();
        if (state.PendingSelection == PendingGroup)
        {
            return await SelectGroup(number, state);
        }

        if (state.PendingSelection == PendingNode)
        {
            return await SelectNode(number, state);
        }

        throw new InvalidOperationException("Number selection is only valid immediately after `vpn list` or `vpn group`.");
    }

    private static async Task<Dictionary<string, object?>> SelectGroup(int number, PluginState state)
    {
        var snapshot = await FetchAllNodes();
        var groups = BuildGroups(snapshot, state.CurrentGroupKey);
        var group = groups.FirstOrDefault(t => t.Number == number)
                    ?? throw new InvalidOperationException($"No group numbered {number}. Run `vpn group` again.");

        state.CurrentGroupKey = group.Key;
        state.CurrentGroupName = group.Name;
        state.PendingSelection = null;
        SaveState(state);

        return TextResult($"Current group set to [{group.Number}] {group.Name}. Run `vpn list` to show its nodes.");
    }

    private static async Task<Dictionary<string, object?>> SelectNode(int number, PluginState state)
    {
        var snapshot = await FetchAllNodes();
        var groups = BuildGroups(snapshot, state.CurrentGroupKey);
        EnsureCurrentGroup(state, groups);

        var nodes = BuildNodeViews(snapshot, state.CurrentGroupKey);
        var node = nodes.FirstOrDefault(t => t.Number == number)
                   ?? throw new InvalidOperationException($"No node numbered {number} in current group `{state.CurrentGroupName}`. Run `vpn list` again.");

        var result = await CallPipe("switch", new Dictionary<string, object?>
        {
            ["indexId"] = node.IndexId
        }, TimeSpan.FromSeconds(10));
        if (!IsOk(result))
        {
            return TextResult(ToPrettyJson(result));
        }

        state.PendingSelection = null;
        SaveState(state);

        return TextResult($"Switched to [{node.Number}] {node.Name}\nGroup: {state.CurrentGroupName}\n\n{ToPrettyJson(result)}");
    }

    private static async Task<Dictionary<string, object?>> ReloadPlugin()
    {
        var result = await CallPipe("reload", new Dictionary<string, object?>(), TimeSpan.FromSeconds(10));
        return TextResult(ToPrettyJson(result));
    }

    private static Dictionary<string, object?> RemovedCommand(string message)
    {
        return TextResult(message);
    }

    private static async Task<NodeSnapshot> FetchAllNodes()
    {
        var result = await CallPipe("list", new Dictionary<string, object?>(), TimeSpan.FromSeconds(10));
        if (!IsOk(result))
        {
            throw new InvalidOperationException($"v2rayN list failed:\n{ToPrettyJson(result)}");
        }

        var data = result["data"] as JsonObject;
        var nodes = data?["nodes"] as JsonArray ?? new JsonArray();
        var currentIndexId = data?["currentIndexId"]?.ToString() ?? string.Empty;
        return new NodeSnapshot(nodes, currentIndexId);
    }

    private static List<GroupView> BuildGroups(NodeSnapshot snapshot, string? currentGroupKey)
    {
        var groups = new List<(string Key, string Name, int Count, bool ContainsCurrentNode)>();
        foreach (var item in snapshot.Nodes)
        {
            if (item is not JsonObject node)
            {
                continue;
            }

            var key = GetGroupKey(node);
            var name = GetGroupName(node);
            var containsCurrentNode = IsCurrentNode(node, snapshot.CurrentIndexId);
            var index = groups.FindIndex(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            if (index < 0)
            {
                groups.Add((key, name, 1, containsCurrentNode));
            }
            else
            {
                var group = groups[index];
                groups[index] = (group.Key, group.Name, group.Count + 1, group.ContainsCurrentNode || containsCurrentNode);
            }
        }

        return groups
            .Select((group, index) => new GroupView(
                index + 1,
                group.Key,
                group.Name,
                group.Count,
                string.Equals(group.Key, currentGroupKey, StringComparison.Ordinal),
                group.ContainsCurrentNode))
            .ToList();
    }

    private static void EnsureCurrentGroup(PluginState state, List<GroupView> groups)
    {
        if (groups.Count == 0)
        {
            throw new InvalidOperationException("No v2rayN groups or nodes were returned.");
        }

        var current = groups.FirstOrDefault(t => string.Equals(t.Key, state.CurrentGroupKey, StringComparison.Ordinal))
                      ?? groups.FirstOrDefault(t => t.ContainsCurrentNode)
                      ?? groups[0];

        state.CurrentGroupKey = current.Key;
        state.CurrentGroupName = current.Name;
    }

    private static List<NodeView> BuildNodeViews(NodeSnapshot snapshot, string? groupKey)
    {
        var nodes = new List<NodeView>();
        foreach (var item in snapshot.Nodes)
        {
            if (item is not JsonObject node)
            {
                continue;
            }

            var nodeGroupKey = GetGroupKey(node);
            if (!string.Equals(nodeGroupKey, groupKey, StringComparison.Ordinal))
            {
                continue;
            }

            var indexId = GetNodeString(node, "indexId");
            nodes.Add(new NodeView(
                nodes.Count + 1,
                node,
                indexId,
                GetNodeString(node, "name"),
                nodeGroupKey,
                GetGroupName(node),
                IsCurrentNode(node, snapshot.CurrentIndexId)));
        }

        return nodes;
    }

    private static async Task<TestSummary> StartCurrentGroupTest(List<NodeView> nodes)
    {
        var indexIds = nodes
            .Select(t => t.IndexId)
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (indexIds.Count == 0)
        {
            return new TestSummary(false, "no testable nodes", new Dictionary<string, JsonObject>());
        }

        var result = await CallPipe("test", new Dictionary<string, object?>
        {
            ["action"] = "realping",
            ["indexIds"] = indexIds
        }, TimeSpan.FromSeconds(10));
        if (!IsOk(result))
        {
            return new TestSummary(false, $"failed to start ({CompactError(result)})", new Dictionary<string, JsonObject>());
        }

        var updates = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow + TestCallbackWait;
        while (DateTime.UtcNow < deadline && CountFinalDelayUpdates(indexIds, updates) < indexIds.Count)
        {
            await Task.Delay(500);
            try
            {
                var status = await CallPipe("test-status", new Dictionary<string, object?>(), TimeSpan.FromSeconds(2));
                MergeUpdates(status, indexIds, updates);
            }
            catch
            {
                break;
            }
        }

        var completed = CountFinalDelayUpdates(indexIds, updates);
        return new TestSummary(true, $"realping {completed}/{indexIds.Count} completed", updates);
    }

    private static int CountFinalDelayUpdates(List<string> indexIds, Dictionary<string, JsonObject> updates)
    {
        return indexIds.Count(indexId => updates.TryGetValue(indexId, out var update) && HasFinalDelay(update));
    }

    private static bool HasFinalDelay(JsonObject update)
    {
        var delay = GetNodeString(update, "delay");
        if (string.IsNullOrWhiteSpace(delay))
        {
            return false;
        }

        if (long.TryParse(delay, out _))
        {
            return true;
        }

        return !IsTestingText(delay);
    }

    private static void MergeUpdates(JsonObject status, List<string> indexIds, Dictionary<string, JsonObject> updates)
    {
        var allowed = indexIds.ToHashSet(StringComparer.Ordinal);
        var data = status["data"] as JsonObject;
        var updateArray = data?["updates"] as JsonArray;
        if (updateArray is null)
        {
            return;
        }

        foreach (var item in updateArray)
        {
            if (item is not JsonObject update)
            {
                continue;
            }

            var indexId = GetNodeString(update, "indexId");
            if (allowed.Contains(indexId))
            {
                updates[indexId] = update;
            }
        }
    }

    private static async Task<JsonObject> CallPipe(string command, Dictionary<string, object?> arguments, TimeSpan? timeout = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("v2rayN pipe control is only available on Windows.");
        }

        var payload = new Dictionary<string, object?>(arguments)
        {
            ["cmd"] = command
        };
        return await CallPipe(payload, timeout ?? TimeSpan.FromSeconds(5));
    }

    private static async Task<JsonObject> CallPipe(Dictionary<string, object?> payload, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            var remainingMs = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    GetPipeName(),
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await Task.Run(() => pipe.Connect(Math.Min(remainingMs, 250)));

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, true)
                {
                    AutoFlush = true
                };

                await writer.WriteLineAsync(JsonSerializer.Serialize(payload, CompactJson));

                var readTask = reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(remainingMs));
                if (completed != readTask)
                {
                    throw new TimeoutException("Timed out waiting for v2rayN IPC response.");
                }

                var line = await readTask;
                if (string.IsNullOrWhiteSpace(line))
                {
                    throw new IOException("v2rayN pipe closed without a response.");
                }

                return JsonNode.Parse(line) as JsonObject
                       ?? throw new JsonException("v2rayN returned a non-object JSON response.");
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                lastError = ex;
                await Task.Delay(Math.Min(150, Math.Max(1, remainingMs)));
            }
        }

        throw new InvalidOperationException(
            $"Could not connect to \\\\.\\pipe\\{GetPipeName()}. Start the custom v2rayN build with IPC enabled.",
            lastError);
    }

    private static string CompactNodeTable(List<NodeView> nodes, Dictionary<string, JsonObject> updates)
    {
        var headers = new[] { "No", "Cur", "Delay", "Speed", "Name" };
        var rows = new List<string[]>();

        foreach (var node in nodes)
        {
            updates.TryGetValue(node.IndexId, out var update);
            rows.Add(new[]
            {
                node.Number.ToString(),
                node.Current ? "*" : string.Empty,
                FormatDelay(node.Node, update),
                FormatSpeed(node.Node, update),
                node.Name
            });
        }

        return CompactTable(headers, rows);
    }

    private static string CompactGroupTable(List<GroupView> groups)
    {
        var headers = new[] { "No", "Cur", "Nodes", "Group" };
        var rows = groups
            .Select(group => new[]
            {
                group.Number.ToString(),
                group.Current ? "*" : string.Empty,
                group.Count.ToString(),
                group.Name
            })
            .ToList();

        return CompactTable(headers, rows);
    }

    private static string CompactAvailableTable(List<(int Number, NodeView Node, long Delay)> nodes)
    {
        var headers = new[] { "No", "Orig", "Cur", "Delay", "Name" };
        var rows = nodes
            .Select(item => new[]
            {
                item.Number.ToString(),
                item.Node.Number.ToString(),
                item.Node.Current ? "*" : string.Empty,
                $"{item.Delay} ms",
                item.Node.Name
            })
            .ToList();

        return CompactTable(headers, rows);
    }

    private static string CompactTable(string[] headers, List<string[]> rows)
    {
        var widths = headers.Select(static h => h.Length).ToArray();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Min(Math.Max(widths[i], row[i].Length), 48);
            }
        }

        var lines = new List<string>
        {
            JoinRow(headers, widths),
            JoinRow(widths.Select(static width => new string('-', width)).ToArray(), widths)
        };

        foreach (var row in rows)
        {
            lines.Add(JoinRow(row.Select((value, index) => TrimToWidth(value, widths[index])).ToArray(), widths));
        }

        return string.Join('\n', lines);
    }

    private static string JoinRow(string[] row, int[] widths)
    {
        return string.Join("  ", row.Select((value, index) => value.PadRight(widths[index])));
    }

    private static string TrimToWidth(string value, int width)
    {
        return value.Length <= width ? value : value[..Math.Max(0, width - 3)] + "...";
    }

    private static string FormatDelay(JsonObject node, JsonObject? update)
    {
        var updateDelay = GetNodeString(update, "delay");
        if (!string.IsNullOrWhiteSpace(updateDelay))
        {
            return NormalizeDelay(updateDelay);
        }

        return TryGetLong(node["delay"], out var delay) && delay > 0 ? $"{delay} ms" : "testing";
    }

    private static bool TryGetUsableDelay(JsonObject node, JsonObject? update, out long delay)
    {
        delay = 0;
        var updateDelay = GetNodeString(update, "delay");
        if (!string.IsNullOrWhiteSpace(updateDelay) && long.TryParse(updateDelay, out delay))
        {
            return delay > 0;
        }

        if (TryGetLong(node["delay"], out delay))
        {
            return delay > 0;
        }

        return false;
    }

    private static string FormatSpeed(JsonObject node, JsonObject? update)
    {
        var updateSpeed = GetNodeString(update, "speed");
        if (!string.IsNullOrWhiteSpace(updateSpeed))
        {
            return updateSpeed;
        }

        if (TryGetLong(node["speed"], out var speed) && speed > 0)
        {
            return speed.ToString();
        }

        return GetNodeString(node, "speedMessage");
    }

    private static string NormalizeDelay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "testing";
        }

        if (long.TryParse(value, out var delay))
        {
            if (delay > 0)
            {
                return $"{delay} ms";
            }

            if (delay < 0)
            {
                return "failed";
            }

            return "testing";
        }

        return value;
    }

    private static bool IsTestingText(string value)
    {
        var normalized = value.Trim();
        return normalized.Contains("testing", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("wait", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("测试中", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("等待", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitCommand(string command)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        char? quote = null;

        foreach (var ch in command)
        {
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }
                else
                {
                    builder.Append(ch);
                }
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static string HelpText()
    {
        return string.Join('\n', new[]
        {
            "vpn commands:",
            "  vpn help       Show this help.",
            "  vpn ping       Check v2rayN IPC availability.",
            "  vpn group      List groups; enter the next number to set the plugin current group.",
            "  vpn list       List nodes in the current group and auto-start realping.",
            "  vpn auto N     Test current-group nodes and switch to the Nth usable node.",
            "  N              After vpn list, switch to node N in the current group.",
            "  N              After vpn group, set current group to group N.",
            "  vpn reload     Reload v2rayN IPC plugin DLL only; does not reload core.",
            "",
            "Removed: vpn switch, vpn test, vpn status, vpn stop."
        });
    }

    private static Dictionary<string, object?> TextResult(string text)
    {
        return new Dictionary<string, object?>
        {
            ["content"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
    }

    private static async Task WriteHookJson(object message)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message, CompactJson));
        await Console.Out.FlushAsync();
    }

    private static bool ShouldHandleHookPrompt(string prompt)
    {
        var tokens = SplitCommand(prompt);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (string.Equals(tokens[0], "vpn", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (tokens.Count == 1 && int.TryParse(tokens[0], out _))
        {
            var state = LoadState();
            return state.PendingSelection is PendingNode or PendingGroup;
        }

        return false;
    }

    private static void ClearPendingSelection()
    {
        var state = LoadState();
        if (state.PendingSelection is null)
        {
            return;
        }

        state.PendingSelection = null;
        SaveState(state);
    }

    private static string ExtractTextResult(Dictionary<string, object?> result)
    {
        if (!result.TryGetValue("content", out var contentValue) || contentValue is not IEnumerable<object> contentItems)
        {
            return JsonSerializer.Serialize(result, PrettyJson);
        }

        var parts = new List<string>();
        foreach (var item in contentItems)
        {
            if (item is not Dictionary<string, object?> content)
            {
                continue;
            }

            if (content.TryGetValue("text", out var text) && text is not null)
            {
                parts.Add(text.ToString() ?? string.Empty);
            }
        }

        return parts.Count == 0 ? JsonSerializer.Serialize(result, PrettyJson) : string.Join("\n", parts);
    }

    private static string ExtractEffectivePrompt(string prompt)
    {
        var normalized = prompt.Replace("\r\n", "\n");
        const string marker = "## My request for Codex:";
        var markerIndex = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? prompt : normalized[(markerIndex + marker.Length)..];
    }

    private static PluginState LoadState()
    {
        try
        {
            var path = StatePath();
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PluginState>(File.ReadAllText(path), CompactJson) ?? new PluginState();
            }
        }
        catch
        {
        }

        return new PluginState();
    }

    private static void SaveState(PluginState state)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        var path = StatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, PrettyJson), new UTF8Encoding(false));
    }

    private static string StatePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "CodexV2rayNControl", "state.json");
    }

    private static string GetPipeName()
    {
        var value = Environment.GetEnvironmentVariable("V2RAYN_CONTROL_PIPE");
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPipeName;
        }

        const string pipePrefix = "\\\\.\\pipe\\";
        return value.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase)
            ? value[pipePrefix.Length..]
            : value;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsOk(JsonObject result)
    {
        try
        {
            return result["ok"]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToPrettyJson(JsonNode node)
    {
        return node.ToJsonString(PrettyJson);
    }

    private static string CompactError(JsonObject result)
    {
        var error = result["error"] as JsonObject;
        var message = error?["message"]?.ToString();
        return string.IsNullOrWhiteSpace(message) ? "unknown error" : message;
    }

    private static string GetGroupKey(JsonObject node)
    {
        var groupId = GetNodeString(node, "groupId");
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            return groupId;
        }

        var group = GetNodeString(node, "group");
        return string.IsNullOrWhiteSpace(group) ? UngroupedKey : group;
    }

    private static string GetGroupName(JsonObject node)
    {
        var group = GetNodeString(node, "group");
        return string.IsNullOrWhiteSpace(group) ? UngroupedName : group;
    }

    private static bool IsCurrentNode(JsonObject node, string currentIndexId)
    {
        return GetNodeBool(node, "current")
               || (!string.IsNullOrWhiteSpace(currentIndexId)
                   && string.Equals(GetNodeString(node, "indexId"), currentIndexId, StringComparison.Ordinal));
    }

    private static string GetNodeString(JsonObject? node, string name)
    {
        return node?[name]?.ToString() ?? string.Empty;
    }

    private static bool GetNodeBool(JsonObject? node, string name)
    {
        try
        {
            return node?[name]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<long>();
            return true;
        }
        catch
        {
            return long.TryParse(node.ToString(), out value);
        }
    }
}
