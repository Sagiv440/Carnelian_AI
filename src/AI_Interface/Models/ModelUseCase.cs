namespace AI_Interface.Models;

/// <summary>What a user mainly wants a model for — drives the Model Config recommendations.</summary>
public enum ModelUseCase
{
    /// <summary>General-purpose / all-round assistant.</summary>
    Standard,

    /// <summary>Code generation and completion.</summary>
    Coding,

    /// <summary>Conversation / lightweight everyday chat.</summary>
    Chat,

    /// <summary>Multimodal models that can see images.</summary>
    Vision,

    /// <summary>Models with native chain-of-thought / step-by-step reasoning.</summary>
    Reasoning
}
