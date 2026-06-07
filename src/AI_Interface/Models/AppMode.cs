namespace AI_Interface.Models;

/// <summary>How a user prompt is handled when sent.</summary>
public enum AppMode
{
    /// <summary>Plain conversation with the local model.</summary>
    Chat,

    /// <summary>One round of web search injected as context before the model answers.</summary>
    WebSearch,

    /// <summary>Multi-step research: the model plans queries, the app searches and reads pages, then the model synthesizes a cited answer.</summary>
    DeepResearch,

    /// <summary>Project agent: the model uses tools to create/delete files and folders and run terminal commands inside the active project directory.</summary>
    Project
}
