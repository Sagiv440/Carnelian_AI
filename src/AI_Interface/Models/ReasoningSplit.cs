using System;
using System.Text;

namespace AI_Interface.Models;

/// <summary>
/// Splits raw model output into its hidden reasoning and its visible answer. Reasoning models
/// (qwen3, deepseek-r1, …) wrap their chain-of-thought in <c>&lt;think&gt;…&lt;/think&gt;</c>; this pulls
/// that out so the UI can show it in a separate collapsible block. Re-runnable on a growing stream:
/// an unterminated <c>&lt;think&gt;</c> at the end (still streaming) is treated as reasoning.
/// </summary>
public static class ReasoningSplit
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    public static (string Reasoning, string Answer) Split(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return ("", "");

        var reasoning = new StringBuilder();
        var answer = new StringBuilder();
        var i = 0;

        while (i < raw.Length)
        {
            var open = raw.IndexOf(Open, i, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                answer.Append(raw, i, raw.Length - i);
                break;
            }

            answer.Append(raw, i, open - i);
            var afterOpen = open + Open.Length;

            var close = raw.IndexOf(Close, afterOpen, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                // Unterminated block — the model is still streaming its thoughts.
                if (reasoning.Length > 0) reasoning.Append('\n');
                reasoning.Append(raw, afterOpen, raw.Length - afterOpen);
                break;
            }

            if (reasoning.Length > 0) reasoning.Append('\n');
            reasoning.Append(raw, afterOpen, close - afterOpen);
            i = close + Close.Length;
        }

        return (reasoning.ToString().Trim(), answer.ToString().Trim());
    }
}
