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
    /// <summary>Fallback hard cap on tool-use rounds when a caller doesn't specify one (matches Guided).</summary>
    private const int DefaultMaxSteps = AutonomyMap.GuidedSteps;

    /// <summary>Output kept per tool result (both for the model and the transcript).</summary>
    private const int MaxResultChars = 6000;

    /// <summary>Wall-clock limit for a single terminal command.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private readonly IMemoryService _memory;
    private readonly IProjectDocsService _docs;

    public ProjectAgentService(IMemoryService memory, IProjectDocsService docs)
    {
        _memory = memory;
        _docs = docs;
    }

    /// <summary>
    /// Maintenance directive appended to the system prompt when the agent owns the handbook (top-level run).
    /// Mirrors how Claude Code keeps CLAUDE.md: durable rules go here, transient facts go to memory.
    /// </summary>
    private const string DocsDirective =
        "\n\nYou maintain this project's handbook (.AI/AI_DOCS.md) with the update_docs tool: update it when a " +
        "durable rule, convention, architecture fact, or command changes — keep it concise and accurate. " +
        "It is rules, not a log (use memory for transient facts).";

    /// <summary>
    /// The update_docs tool's model-facing description, encoding the CLAUDE.md-maintenance discipline. Shared
    /// with <see cref="AgentOrchestrator"/> so the lead and the single agent advertise an identical tool.
    /// </summary>
    internal const string UpdateDocsToolDescription =
        "Create or update this project's handbook at .AI/AI_DOCS.md — the authoritative 'how this project " +
        "works' guide you and other agents read every turn. Submit the COMPLETE new handbook in `content` " +
        "(it replaces the file). Rules (same discipline a developer uses for a CLAUDE.md): (1) Update only " +
        "when a DURABLE rule, convention, architecture fact, or build/run/test command changes — things a " +
        "future run must know. (2) It is RULES/orientation, NOT a log or scratchpad — transient notes and " +
        "learned facts go to memory (the remember tool), never here. (3) Keep it concise and accurate; a " +
        "stale or bloated handbook is worse than none. (4) You can see the current handbook in your context " +
        "— revise it surgically, preserving useful existing content, rather than discarding it. (5) Don't " +
        "update for trivial or one-off things.";

    /// <summary>The JSON schema for the update_docs tool's single required <c>content</c> argument. Shared with the orchestrator.</summary>
    internal static JsonElement UpdateDocsSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            content = new { type = "string", description = "The COMPLETE new handbook markdown (it replaces the file)." }
        },
        required = new[] { "content" }
    });

    public async Task RunAsync(
        IChatClient client,
        Project project,
        string model,
        IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode,
        int maxSteps,
        AgentTools allowedTools,
        string personaPrefix,
        string thinkingDirective,
        string projectSkills,
        SoftwareInstallPermission installPermission,
        bool memoryEnabled,
        bool allowDocsUpdate,
        IProgress<string> status,
        Action<string> onActivity,
        Action<ActivityUpdate>? onActivityStep,
        Action<string> onAnswer,
        Func<ToolApprovalRequest, Task<bool>> approve,
        CancellationToken ct)
    {
        // A null allow-list (e.g. an agent with no tool profile) is treated as unrestricted.
        allowedTools ??= new AgentTools();
        // The step budget is set by the active agent's autonomy level; fall back to the Guided default.
        if (maxSteps <= 0)
            maxSteps = DefaultMaxSteps;
        var tools = BuildTools(allowedTools, installPermission, memoryEnabled, allowDocsUpdate);

        var messages = new List<ChatMessage>
        {
            // The active agent's persona sits on top of the service-owned sandbox prompt. The handbook
            // maintenance directive is added only when this is a top-level (main) run that owns update_docs.
            ChatMessage.System(personaPrefix + SystemPrompt(project, installPermission) +
                (allowDocsUpdate ? DocsDirective : "") + thinkingDirective + projectSkills)
        };
        messages.AddRange(conversation);

        // 0-based counter correlating each tool call's Started/Finished structured update (mirrors the
        // orchestrator's delegation index). Notes consume an index too so each lands in the feed in order.
        var activityIndex = 0;

        for (var step = 0; step < maxSteps; step++)
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
            {
                onActivity(turn.Content + "\n");
                // Structured feed: the interim narration as a lightweight "note" line.
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Note, activityIndex++, "", "", "", turn.Content.Trim(), false));
            }

            foreach (var call in turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                // Structured feed: emit a "Started" row before running the tool (derived from the same pure
                // Describe(...) the inline log uses). ExecuteAsync itself is unchanged — it still emits the
                // 🔧 strings via onActivity, which the VM hides for project runs once the structured feed exists.
                var (summary, detail, _) = Describe(project, call, GetString(call.Arguments, "path"));
                var idx = activityIndex++;
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Started, idx, IconFor(call.Name), summary, detail, "", false));

                var result = await ExecuteAsync(
                    project, call, approvalMode, allowedTools, installPermission, allowDocsUpdate,
                    status, onActivity, approve, ct)
                    .ConfigureAwait(false);

                // Structured feed: resolve the row with this tool's result + success/failure status.
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Finished, idx, "", "", "", result, IsFailure(result)));

                messages.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });
            }
        }

        onAnswer($"_(stopped after {maxSteps} steps — the task may be unfinished)_");
    }

    // ---- the agent loop's single-tool step -------------------------------------------------

    private async Task<string> ExecuteAsync(
        Project project, AgentToolCall call, AgentApprovalMode approvalMode,
        AgentTools allowedTools, SoftwareInstallPermission installPermission, bool allowDocsUpdate,
        IProgress<string> status, Action<string> onActivity,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        var path = GetString(call.Arguments, "path");
        var (summary, detail, destructive) = Describe(project, call, path);

        onActivity($"\n🔧 {summary}{(string.IsNullOrEmpty(detail) ? "" : $"  `{detail}`")}\n");

        // Defense in depth: update_docs is offered only to the top-level (main) agent and isn't gated by the
        // AgentTools allow-list below, so refuse it here for a delegated specialist that calls it anyway.
        if (call.Name == "update_docs" && !allowDocsUpdate)
        {
            onActivity("   ⛔ blocked — only the main agent may update the project handbook\n");
            return "Only the main (top-level) agent may update the project handbook (.AI/" +
                   ProjectDocsService.FileName + "). A delegated specialist can't.";
        }

        // Defense in depth: even though disallowed tools aren't advertised, refuse one if the model calls
        // it anyway (e.g. it hallucinated a tool name) rather than silently running it.
        if (ToolGroupOf(call.Name) is { } group && !allowedTools.Allows(group))
        {
            onActivity($"   ⛔ blocked — this agent isn't allowed to {PermissionLabel(group)}\n");
            return $"This agent is not permitted to {PermissionLabel(group)}. The user can enable this in " +
                   "Settings → AI Features → Agents (Tool permissions) for a custom agent, then retry.";
        }

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

        // Live "current action" (Phase 3A): show the actual step (icon · summary · target) in the busy
        // status bar, so the user can see what's running without opening the activity feed.
        status.Report(CurrentActionLabel(call.Name, summary, detail));

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
                "remember"       => Remember(project, GetString(call.Arguments, "text"), GetString(call.Arguments, "scope")),
                "create_skill"   => CreateSkill(project, GetString(call.Arguments, "name"),
                                        GetString(call.Arguments, "content"), GetString(call.Arguments, "description")),
                "update_docs"    => UpdateDocs(project, GetString(call.Arguments, "content")),
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
        "remember"       => ("Remember a note", GetString(call.Arguments, "text") ?? "", false),
        "create_skill"   => ("Create project skill", GetString(call.Arguments, "name") ?? "", false),
        "update_docs"    => ("Update project handbook", ".AI/" + ProjectDocsService.FileName, true),
        _ => (call.Name, "", true)
    };

    /// <summary>
    /// A compact one-line label for the live "current action" status bar (Phase 3A): the tool's glyph +
    /// summary, plus a shortened target/command when present — e.g. "⌘ Run command · npm run build". Pure
    /// and deterministic: the detail is collapsed to a single line and capped so a long command can't blow
    /// out the status line (the bar also ellipsizes, but the cap keeps the string itself bounded).
    /// </summary>
    internal static string CurrentActionLabel(string tool, string summary, string detail)
    {
        var icon = IconFor(tool);
        var d = (detail ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (d.Length == 0)
            return $"{icon} {summary}";

        const int max = 80;
        if (d.Length > max)
            d = d[..max] + "…";
        return $"{icon} {summary} · {d}";
    }

    /// <summary>Tool glyph for the structured activity feed's "Started" row.</summary>
    internal static string IconFor(string tool) => tool switch
    {
        "list_directory"   => "📂",
        "read_file"        => "📄",
        "write_file"       => "✏️",
        "create_folder"    => "📁",
        "delete_file"      => "🗑",
        "delete_folder"    => "🗑",
        "run_command"      => "⌘",
        "install_software" => "📦",
        "remember"         => "💾",
        "create_skill"     => "✨",
        "update_docs"      => "📘",
        _                  => "🔧"
    };

    /// <summary>
    /// Whether a tool <paramref name="result"/> string indicates the call didn't succeed (drives the ✗ vs ✓
    /// status glyph). Heuristic on the markers <see cref="ExecuteAsync"/> and the tool helpers return: an
    /// "Error:" prefix, the refusal/guard phrases (declined, not permitted, disabled, refusing, handbook-guard),
    /// and the operational failures (not-found, command-start failure, timeout, sandbox-escape block). Markers
    /// are deliberately distinctive (e.g. "not found:" with the colon) to avoid matching file/command output
    /// that merely mentions the words.
    /// </summary>
    internal static bool IsFailure(string result)
    {
        if (string.IsNullOrEmpty(result))
            return false;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var marker in FailureMarkers)
            if (result.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static readonly string[] FailureMarkers =
    {
        "declined to run", "not permitted", "is disabled", "Refusing to",
        "can only be changed with the update_docs",
        "not found:", "Failed to start command", "timed out after", "was blocked"
    };

    /// <summary>Persists a fact to memory. <c>scope</c> "user" → global; anything else (default) → this project.</summary>
    private string Remember(Project project, string? text, string? scope)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return "Nothing to remember (no text was provided).";

        if (string.Equals(scope?.Trim(), "user", StringComparison.OrdinalIgnoreCase))
        {
            _memory.Add(MemoryScope.Global, text, "project-agent", project.Directory);
            return $"Remembered (about the user): {text}";
        }

        _memory.Add(MemoryScope.Project, text, "project-agent", project.Directory);
        return $"Remembered (this project): {text}";
    }

    /// <summary>
    /// Writes a reusable project skill to <c>&lt;project&gt;/.AI/skills/&lt;slug&gt;.skill.md</c> (frontmatter +
    /// the model-authored body). The path is computed here from a slugified name, so the model can't write
    /// outside the skills folder. Files here load as project guidance the next time the project is opened.
    /// </summary>
    private static string CreateSkill(Project project, string? name, string? content, string? description)
    {
        name = (name ?? "").Trim();
        content = (content ?? "").Trim();
        if (name.Length == 0 || content.Length == 0)
            return "Provide both a skill 'name' and 'content'.";

        var dir = Path.Combine(project.Directory, ".AI", "skills");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Slugify(name) + ".skill.md");

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(name.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
        if (!string.IsNullOrWhiteSpace(description))
            sb.Append("description: ").Append(description!.Replace('\n', ' ').Replace('\r', ' ').Trim()).Append('\n');
        sb.Append("---\n\n");
        sb.Append(content).Append('\n');
        File.WriteAllText(file, sb.ToString());

        return $"Created skill '{name}' at {Rel(project, file)} ({content.Length} chars). " +
               "It loads as project guidance the next time this project is opened.";
    }

    /// <summary>
    /// Creates or overwrites the project handbook at <c>.AI/AI_DOCS.md</c> with the model-authored Markdown.
    /// This is the SOLE writer of that file (write_file/delete_file/delete_folder refuse it), so the handbook
    /// stays under a single sanctioned, approval-gated path. It loads as authoritative project guidance on the
    /// next turn (the VM re-scans after every project-agent turn).
    /// </summary>
    private string UpdateDocs(Project project, string? content)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
            return "Provide non-empty 'content' — the COMPLETE new handbook markdown to write.";

        _docs.Save(project.Directory, content);
        return $"Updated the project handbook (.AI/{ProjectDocsService.FileName}) — {content.Length} chars. " +
               "It loads as authoritative project guidance on the next turn.";
    }

    /// <summary>Lowercases a name and reduces it to a safe filename slug (letters/digits → '-' runs collapsed).</summary>
    private static string Slugify(string s)
    {
        var slug = new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length == 0 ? "skill" : slug;
    }

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

    /// <summary>Maps a tool name to the <see cref="AgentToolGroup"/> that gates it (null = always allowed).</summary>
    private static AgentToolGroup? ToolGroupOf(string toolName) => toolName switch
    {
        "list_directory" or "read_file"     => AgentToolGroup.ReadFiles,
        "write_file" or "create_folder"     => AgentToolGroup.WriteFiles,
        "delete_file" or "delete_folder"    => AgentToolGroup.DeleteFiles,
        "run_command"                       => AgentToolGroup.RunCommands,
        "install_software"                  => AgentToolGroup.InstallSoftware,
        "update_docs"                       => null, // writes only to the fixed handbook path — not allow-list-gated
        _                                   => null
    };

    private static string PermissionLabel(AgentToolGroup group) => group switch
    {
        AgentToolGroup.ReadFiles       => "read files or list directories",
        AgentToolGroup.WriteFiles      => "create or write files and folders",
        AgentToolGroup.DeleteFiles     => "delete files or folders",
        AgentToolGroup.RunCommands     => "run terminal commands",
        AgentToolGroup.InstallSoftware => "install software",
        _                              => "use that tool"
    };

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
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
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
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";
        File.Delete(full);
        return $"Deleted file {Rel(project, full)}.";
    }

    private static string DeleteFolder(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        if (string.Equals(Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar),
                           Path.GetFullPath(project.Directory).TrimEnd(Path.DirectorySeparatorChar),
                           PathComparison))
            return "Refusing to delete the project root directory itself.";
        if (ContainsHandbook(project, full))
            return "Refusing to delete the .AI folder — it holds the project handbook (.AI/" +
                   ProjectDocsService.FileName + "), skills, agents, and memory. Use update_docs to change the handbook.";
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

    /// <summary>Message returned when a generic write/delete tool targets the handbook (use update_docs instead).</summary>
    private const string HandbookGuardMessage =
        "The project handbook (.AI/" + ProjectDocsService.FileName + ") can only be changed with the update_docs tool.";

    /// <summary>
    /// True when <paramref name="fullPath"/> is the project handbook (.AI/AI_DOCS.md). The generic
    /// write_file/delete_file/delete_folder tools refuse it so update_docs stays the sole writer — this stops
    /// any agent (including a delegated specialist that lacks update_docs) from clobbering the handbook.
    /// </summary>
    internal static bool IsHandbookPath(Project project, string fullPath)
    {
        var handbook = Path.GetFullPath(Path.Combine(project.Directory, ".AI", ProjectDocsService.FileName));
        return string.Equals(
            Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar),
            handbook.TrimEnd(Path.DirectorySeparatorChar),
            PathComparison);
    }

    /// <summary>
    /// True when <paramref name="fullDir"/> is a directory that CONTAINS the handbook (the <c>.AI</c> folder,
    /// or an ancestor of it). Lets delete_folder refuse wiping the handbook — and the rest of <c>.AI</c>
    /// (skills, agents, memory, chats) — by deleting a parent folder rather than the file itself.
    /// </summary>
    internal static bool ContainsHandbook(Project project, string fullDir)
    {
        var handbook = Path.GetFullPath(Path.Combine(project.Directory, ".AI", ProjectDocsService.FileName));
        var dir = Path.GetFullPath(fullDir).TrimEnd(Path.DirectorySeparatorChar);
        return handbook.StartsWith(dir + Path.DirectorySeparatorChar, PathComparison);
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

    /// <summary>
    /// Advertises only the tools the active agent is permitted to use. Each tool group is gated by the
    /// agent's <see cref="AgentTools"/> allow-list; <c>install_software</c> additionally requires the global
    /// <see cref="SoftwareInstallPermission"/> to not be <see cref="SoftwareInstallPermission.Never"/> (both
    /// gates must allow). An unrestricted agent (the default) offers the full set, so behaviour is unchanged
    /// when the user hasn't restricted anything.
    /// </summary>
    private static IReadOnlyList<AgentTool> BuildTools(AgentTools allowed, SoftwareInstallPermission installPermission, bool memoryEnabled, bool allowDocsUpdate)
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

        var tools = new List<AgentTool>();

        if (allowed.Allows(AgentToolGroup.ReadFiles))
        {
            tools.Add(new("list_directory",
                "List the files and sub-folders of a directory in the project (path defaults to the root).",
                Schema(new
                {
                    type = "object",
                    properties = new { path = new { type = "string", description = "Directory path relative to the project root. Omit or \".\" for the root." } }
                })));
            tools.Add(new("read_file", "Read and return the full text of a file in the project.", pathOnly));
        }

        if (allowed.Allows(AgentToolGroup.WriteFiles))
        {
            tools.Add(new("write_file",
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
                })));
            tools.Add(new("create_folder", "Create a folder (and any missing parents) in the project.", pathOnly));
        }

        if (allowed.Allows(AgentToolGroup.DeleteFiles))
        {
            tools.Add(new("delete_file", "Delete a file in the project.", pathOnly));
            tools.Add(new("delete_folder", "Delete a folder in the project and everything inside it.", pathOnly));
        }

        if (allowed.Allows(AgentToolGroup.RunCommands))
        {
            tools.Add(new("run_command",
                "Run a terminal command with the project root as the working directory; returns its exit code and output.",
                commandSchema));
        }

        // install_software needs BOTH the agent's permission AND the project's software-install setting.
        if (allowed.Allows(AgentToolGroup.InstallSoftware) && installPermission != SoftwareInstallPermission.Never)
        {
            tools.Add(new AgentTool("install_software",
                "Install software/tooling machine-wide (e.g. a runtime, SDK, CLI, or package-manager package) " +
                "needed to run or build the project. Provide the full install command (e.g. 'winget install ...', " +
                "'apt-get install -y ...', 'brew install ...').",
                commandSchema));
        }

        // create_skill: a meta tool that writes a reusable guidance file under .AI/skills/. Always offered
        // (it only writes to that controlled location), so "create a skill for X" works with any agent.
        tools.Add(new AgentTool("create_skill",
            "Create a reusable project skill file under .AI/skills/ that captures how to work in this project " +
            "(purpose, conventions, architecture, workflow steps, do/don't rules, examples). Use this when the " +
            "user asks to \"create a skill for <subject>\". Write thorough, well-structured Markdown in 'content' " +
            "with clear section headings so future sessions load it as authoritative project guidance.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Short skill title, e.g. \"API conventions\"." },
                    description = new { type = "string", description = "One-line summary of what the skill covers." },
                    content = new { type = "string", description = "The full skill guidance as structured Markdown (sections, do/don't, examples)." }
                },
                required = new[] { "name", "content" }
            })));

        // update_docs: maintains the project handbook (.AI/AI_DOCS.md). Like create_skill it writes only to a
        // fixed, controlled path so it isn't gated by the allow-list — but it's offered ONLY to a top-level
        // (main) agent run (allowDocsUpdate), never to a delegated specialist.
        if (allowDocsUpdate)
            tools.Add(new AgentTool("update_docs", UpdateDocsToolDescription, UpdateDocsSchema()));

        // remember: not a file/command tool, so it isn't gated by the allow-list — only by the memory switch.
        if (memoryEnabled)
        {
            tools.Add(new AgentTool("remember",
                "Save a short, durable fact to recall in future sessions (a user preference or a key detail " +
                "about this project). Use scope 'project' (default) for facts about this project, 'user' for " +
                "facts about the user. Only remember stable, useful facts — never transient or sensitive details.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "The fact to remember, as one short sentence." },
                        scope = new { type = "string", description = "'project' (default) or 'user'." }
                    },
                    required = new[] { "text" }
                })));
        }

        return tools;
    }
}
