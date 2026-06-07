namespace AI_Interface.Models;

/// <summary>Kind of file a user can attach to a prompt.</summary>
public enum AttachmentKind
{
    Photo,

    /// <summary>A document or text file (PDF, DOCX, TXT, MD, code, …) — read as text for the model.</summary>
    Document
}

/// <summary>A file attached to a prompt (selected via the composer's attach button).</summary>
public sealed class Attachment
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required AttachmentKind Kind { get; init; }

    public bool IsPhoto => Kind == AttachmentKind.Photo;

    /// <summary>Small glyph shown for non-image attachments.</summary>
    public string Glyph => Kind == AttachmentKind.Photo ? "🖼" : "📄";
}
