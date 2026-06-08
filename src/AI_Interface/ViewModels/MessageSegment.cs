using System.Collections.Generic;
using System.Text;
using static System.StringComparison;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// One rendered piece of a message: either prose or a fenced code/command block. The transcript
/// shows prose as normal text and code as a monospace bubble with a language label + copy button.
/// <see cref="Text"/> is observable so a streaming code block grows in place (no container churn).
/// </summary>
public sealed partial class MessageSegment : ObservableObject
{
    /// <summary>True for a fenced code/command block; false for prose.</summary>
    public bool IsCode { get; }

    /// <summary>Info string after the opening fence (e.g. <c>bash</c>); empty if none.</summary>
    public string Language { get; }

    [ObservableProperty]
    private string _text;

    public MessageSegment(bool isCode, string language, string text)
    {
        IsCode = isCode;
        Language = language;
        _text = text;
    }

    /// <summary>Header label for a code bubble — the language, or "code" when unspecified.</summary>
    public string LanguageLabel => string.IsNullOrWhiteSpace(Language) ? "code" : Language;
}

/// <summary>
/// Splits message text into prose / fenced-code parts. Fences are triple-backtick lines; an unclosed
/// fence (mid-stream) is treated as code so a block bubbles up as soon as it starts.
/// </summary>
internal static class MarkdownSegmenter
{
    public readonly record struct Part(bool IsCode, string Language, string Text);

    public static List<Part> Parse(string? text)
    {
        var parts = new List<Part>();
        if (string.IsNullOrEmpty(text))
            return parts;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var buffer = new StringBuilder();
        var inCode = false;
        var language = "";

        void FlushText()
        {
            var t = buffer.ToString().Trim('\n');
            if (t.Trim().Length > 0)
                parts.Add(new Part(false, "", t));
            buffer.Clear();
        }

        void FlushCode()
        {
            parts.Add(new Part(true, language, buffer.ToString().Trim('\n')));
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", Ordinal))
            {
                if (!inCode)
                {
                    FlushText();
                    inCode = true;
                    var info = line.TrimStart().TrimStart('`').Trim();
                    language = info.Length == 0 ? "" : info.Split(new[] { ' ', '\t' }, 2)[0];
                }
                else
                {
                    FlushCode();
                    inCode = false;
                    language = "";
                }
                continue;
            }

            buffer.Append(line).Append('\n');
        }

        if (inCode)
            FlushCode(); // unclosed fence while streaming — render the partial block now
        else
            FlushText();

        return parts;
    }
}
