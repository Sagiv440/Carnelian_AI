using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IProjectAgentService"/>. Advertises a small set of file/folder/terminal tools to
/// the model, then loops: ask the model → run the tools it requests (gated by the approval mode and
/// confined to the project directory) → feed results back → repeat until the model answers in plain text.
/// </summary>
public sealed class ProjectAgentService : IProjectAgentService
{
    /// <summary>Hard cap on tool-use rounds so a confused model can't loop forever.</summary>
    private const int MaxSteps = 24;

    /// <summary>Output kept per tool result (both for the model and the transcript).</summary>
    private const int MaxResultChars = 6000;

    /// <summary>Wall-clock limit for a single terminal command.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    public async Task RunAsync(
        IChatClient client,
        Project project,
        string model,
        IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode,
        string personaPrefix,
        string thinkingDirective,
        string projectSkills,
        SoftwareInstallPermission installPermission,
        IProgress<string> status,
        Action<string> onActivity,
        Action<string> onAnswer,
        Func<ToolApprovalRequest, Task<bool>> approve,
        CancellationToken ct)
    {
        var tools = BuildTools(installPermission);

        var messages = new List<ChatMessage>
        {
            // The active agent's persona sits on top of the service-owned sandbox prompt.
            ChatMessage.System(personaPrefix + SystemPrompt(project, installPermission) + thinkingDirective + projectSkills)
        };
        messages.AddRange(conversation);

        for (var step = 0; step < MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report(step == 0 ? "Thinking…" : "Working…");

            var turn = await client.ChatWithToolsAsync(model, messages, tools, ct).ConfigureAwait(false);

            // No tool calls → the model gave its final answer.
            if (turn.ToolCalls.Count == 0)
            {
                onAnswer(string.IsNullOrWhiteSpace(turn.Content) ? "_(no response)_" : turn.Content);
                return;
            }

            // Record the assistant turn (with its tool calls) so the model sees its own request next round.
            messages.Add(new ChatMessage(ChatRole.Assistant, turn.Content) { ToolCalls = turn.ToolCalls });
            // Any text the model emits alongside a tool call is part of its "work", not the final answer.
            if (!string.IsNullOrWhiteSpace(turn.Content))
                onActivity(turn.Content + "\n");

            foreach (var call in turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var result = await ExecuteAsync(
                    project, call, approvalMode, installPermission, status, onActivity, approve, ct)
                    .ConfigureAwait(false);
                messages.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });
            }
        }

        onAnswer($"_(stopped after {MaxSteps} steps — the task may be unfinished)_");
    }

    // ---- the agent loop's single-tool step -------------------------------------------------

    private async Task<string> ExecuteAsync(
        Project project, AgentToolCall call, AgentApprovalMode approvalMode,
        SoftwareInstallPermission installPermission,
        IProgress<string> status, Action<string> onActivity,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        var path = GetString(call.Arguments, "path");
        var (summary, detail, destructive) = Describe(project, call, path);

        onActivity($"\n🔧 {summary}{(string.IsNullOrEmpty(detail) ? "" : $"  `{detail}`")}\n");

        // Is this a machine-wide software install (the install tool, or a run_command that looks like one)?
        var isInstallAction = call.Name == "install_software" ||
            (call.Name == "run_command" && LooksLikeSystemInstall(GetString(call.Arguments, "command") ?? ""));

        // Permission gate: when installs aren't permitted, refuse install actions outright.
        if (isInstallAction && installPermission == SoftwareInstallPermission.Never)
        {
            if (call.Name == "install_software")
            {
                onActivity("   ⛔ blocked — software installation is disabled for this project\n");
                return "Software installation is disabled. Ask the user to allow installs in " +
                       "Settings → Project (\"Ask every time\" or \"Allow agent to install software\"), then retry.";
            }
            onActivity("   ⛔ blocked — that looks like a system install (disabled for this project)\n");
            return "That command installs software machine-wide, which is disabled. Ask the user to allow " +
                   "installs in Settings → Project, then retry.";
        }

        // "Ask every time" forces confirmation for installs even when the approval mode wouldn't.
        var needsApproval = NeedsApproval(approvalMode, destructive) ||
            (isInstallAction && installPermission == SoftwareInstallPermission.Ask);

        if (needsApproval)
        {
            var ok = await approve(new ToolApprovalRequest(call.Name, summary, detail, destructive))
                .ConfigureAwait(false);
            if (!ok)
            {
                onActivity("   ⛔ skipped (you declined this action)\n");
                return "The user declined to run this action.";
            }
        }

        status.Report(summary + "…");

        string result;
        try
        {
            result = call.Name switch
            {
                "list_directory" => ListDirectory(project, path),
                "read_file"      => ReadFile(project, path),
                "write_file"     => WriteFile(project, path, GetString(call.Arguments, "content") ?? ""),
                "create_folder"  => CreateFolder(project, path),
                "delete_file"    => DeleteFile(project, path),
                "delete_folder"  => DeleteFolder(project, path),
                "run_command"    => await RunCommandAsync(project, GetString(call.Arguments, "command") ?? "", ct)
                                        .ConfigureAwait(false),
                "install_software" => await RunCommandAsync(project, GetString(call.Arguments, "command") ?? "", ct)
                                        .ConfigureAwait(false),
                _ => $"Unknown tool '{call.Name}'."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = $"Error: {ex.Message}";
        }

        onActivity(IndentForDisplay(result) + "\n");
        return Truncate(result);
    }

    private static bool NeedsApproval(AgentApprovalMode mode, bool destructive) => mode switch
    {
        AgentApprovalMode.AutoRun => false,
        AgentApprovalMode.ConfirmDestructive => destructive,
        AgentApprovalMode.ConfirmEverything => true,
        _ => destructive
    };

    /// <summary>Human-readable label, the detail to show/confirm, and whether the call is destructive.</summary>
    private static (string Summary, string Detail, bool Destructive) Describe(
        Project project, AgentToolCall call, string? path) => call.Name switch
    {
        "list_directory" => ("List directory", path ?? ".", false),
        "read_file"      => ("Read file", path ?? "", false),
        "create_folder"  => ("Create folder", path ?? "", false),
        "write_file"     => ("Write file", path ?? "", WouldOverwrite(project, path)),
        "delete_file"    => ("Delete file", path ?? "", true),
        "delete_folder"  => ("Delete folder", path ?? "", true),
        "run_command"    => ("Run command", GetString(call.Arguments, "command") ?? "", true),
        "install_software" => ("Install software", GetString(call.Arguments, "command") ?? "", true),
        _ => (call.Name, "", true)
    };

    private static readonly string[] SystemInstallSignatures =
    {
        "winget install", "winget upgrade", "choco install", "choco upgrade", "scoop install",
        "apt install", "apt-get install", "apt install", "dnf install", "yum install",
        "pacman -s", "zypper install", "brew install", "snap install",
        "npm install -g", "npm i -g", "npm install --global", "yarn global add", "pnpm add -g"
    };

    /// <summary>Heuristically detects a machine-wide package-manager install command (to gate run_command).</summary>
    private static bool LooksLikeSystemInstall(string command)
    {
        var c = command.ToLowerInvariant();
        foreach (var sig in SystemInstallSignatures)
            if (c.Contains(sig))
                return true;
        // elevated install on Windows (e.g. "sudo …" handled above for *nix package managers)
        return c.Contains("msiexec") || c.Contains("sudo apt") || c.Contains("sudo dnf") || c.Contains("sudo yum");
    }

    private static bool WouldOverwrite(Project project, string? path) =>
        TryResolve(project.Directory, path, out var full, out _) && File.Exists(full);

    // ---- tools (all confined to the project directory) -------------------------------------

    private static string ListDirectory(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!Directory.Exists(full))
            return $"Directory not found: {Rel(project, full)}";

        var sb = new StringBuilder();
        foreach (var dir in Directory.GetDirectories(full).OrderBy(p => p))
            sb.AppendLine($"[dir]  {Path.GetFileName(dir)}/");
        foreach (var file in Directory.GetFiles(full).OrderBy(p => p))
            sb.AppendLine($"[file] {Path.GetFileName(file)}  ({new FileInfo(file).Length} bytes)");

        var listing = sb.ToString();
        return listing.Length == 0 ? "(empty directory)" : listing.TrimEnd();
    }

    private static string ReadFile(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";
        return File.ReadAllText(full);
    }

    private static string WriteFile(Project project, string? path, string content)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
        return $"Wrote {content.Length} characters to {Rel(project, full)}.";
    }

    private static string CreateFolder(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        Directory.CreateDirectory(full);
        return $"Created folder {Rel(project, full)}.";
    }

    private static string DeleteFile(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";
        File.Delete(full);
        return $"Deleted file {Rel(project, full)}.";
    }

    private static string DeleteFolder(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (string.Equals(Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar),
                           Path.GetFullPath(project.Directory).TrimEnd(Path.DirectorySeparatorChar),
                           PathComparison))
            return "Refusing to delete the project root directory itself.";
        if (!Directory.Exists(full))
            return $"Folder not found: {Rel(project, full)}";
        Directory.Delete(full, recursive: true);
        return $"Deleted folder {Rel(project, full)} (and its contents).";
    }

    private static async Task<string> RunCommandAsync(Project project, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "No command was provided.";

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = project.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c " + command; // cmd handles the rest of the line as the command
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return $"Failed to start command: {ex.Message}";
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            return $"Command timed out after {CommandTimeout.TotalSeconds:0}s and was terminated.";
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw; // user-requested cancellation bubbles up
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"exit code: {process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.Append("stdout:\n").AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.Append("stderr:\n").AppendLine(stderr.TrimEnd());
        return sb.ToString().TrimEnd();
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves a (relative or absolute) path and rejects anything outside the project root.</summary>
    private static bool TryResolve(string root, string? relative, out string full, out string error)
    {
        relative ??= "";
        var rootFull = Path.GetFullPath(root);
        var combined = Path.IsPathRooted(relative) ? relative : Path.Combine(rootFull, relative);
        full = Path.GetFullPath(combined);

        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!string.Equals(full, rootFull, PathComparison) && !full.StartsWith(rootWithSep, PathComparison))
        {
            error = $"Path '{relative}' is outside the project directory and was blocked.";
            full = "";
            return false;
        }

        error = "";
        return true;
    }

    private static string Rel(Project project, string full)
    {
        var rel = Path.GetRelativePath(project.Directory, full);
        return rel == "." ? "(project root)" : rel;
    }

    private static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static string Truncate(string s) =>
        s.Length <= MaxResultChars ? s : s[..MaxResultChars] + "\n…(truncated)";

    /// <summary>Indents a tool result two spaces per line for a readable inline transcript log.</summary>
    private static string IndentForDisplay(string s)
    {
        var trimmed = Truncate(s.TrimEnd());
        var lines = trimmed.Split('\n');
        return string.Join('\n', lines.Select(l => "   " + l));
    }

    private static string SystemPrompt(Project project, SoftwareInstallPermission installPermission)
    {
        var install = installPermission != SoftwareInstallPermission.Never
            ? "- If the project is missing software needed to run or build it (a runtime, SDK, CLI, or " +
              "package manager package), you MAY install it with the install_software tool, which the user " +
              "has permitted for this project. Prefer the platform's package manager."
            : "- You may NOT install software machine-wide. If the project needs a missing runtime/SDK/tool, " +
              "say so and ask the user to allow installs in Settings → Project. " +
              "Installing project-local dependencies (e.g. npm install in the project) is still fine.";

        return $"""
        You are the project agent for "{project.Name}", working inside this directory:
          {project.Directory}

        You have tools to inspect and change the project and to run terminal commands. Rules:
        - All paths are relative to the project root. You cannot read or change anything outside it.
        - Terminal commands run with the project root as the working directory.
        - Inspect before you change: list_directory / read_file before write_file or delete.
        - Make the smallest change that satisfies the request. Do not invent unrelated work.
        {install}
        - When the task is complete, stop calling tools and reply with a short plain-text summary of
          what you did.
        """;
    }

    private static IReadOnlyList<AgentTool> BuildTools(SoftwareInstallPermission installPermission)
    {
        static JsonElement Schema(object o) => JsonSerializer.SerializeToElement(o);

        var pathOnly = Schema(new
        {
            type = "object",
            properties = new { path = new { type = "string", description = "Path relative to the project root." } },
            required = new[] { "path" }
        });

        var commandSchema = Schema(new
        {
            type = "object",
            properties = new { command = new { type = "string", description = "The shell command line to run." } },
            required = new[] { "command" }
        });

        var tools = new List<AgentTool>
        {
            new("list_directory",
                "List the files and sub-folders of a directory in the project (path defaults to the root).",
                Schema(new
                {
                    type = "object",
                    properties = new { path = new { type = "string", description = "Directory path relative to the project root. Omit or \".\" for the root." } }
                })),

            new("read_file", "Read and return the full text of a file in the project.", pathOnly),

            new("write_file",
                "Create a file or overwrite an existing one with the given text content.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "File path relative to the project root." },
                        content = new { type = "string", description = "Full text to write to the file." }
                    },
                    required = new[] { "path", "content" }
                })),

            new("create_folder", "Create a folder (and any missing parents) in the project.", pathOnly),
            new("delete_file", "Delete a file in the project.", pathOnly),
            new("delete_folder", "Delete a folder in the project and everything inside it.", pathOnly),

            new("run_command",
                "Run a terminal command with the project root as the working directory; returns its exit code and output.",
                commandSchema)
        };

        // Offered only when the project permits machine-wide software installation (Ask or Allow).
        if (installPermission != SoftwareInstallPermission.Never)
        {
            tools.Add(new AgentTool("install_software",
                "Install software/tooling machine-wide (e.g. a runtime, SDK, CLI, or package-manager package) " +
                "needed to run or build the project. Provide the full install command (e.g. 'winget install ...', " +
                "'apt-get install -y ...', 'brew install ...').",
                commandSchema));
        }

        return tools;
    }
}
