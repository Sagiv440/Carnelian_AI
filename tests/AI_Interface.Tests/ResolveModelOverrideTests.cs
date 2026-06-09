using System.Collections.Generic;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="MainWindowViewModel.ResolveModelOverride"/> — the pure, I/O-free helper
/// that resolves a persisted "{provider}:{id}" Deep Research model override against the live model
/// list. It parses the setting (via the same logic as <c>ParseSavedModel</c>) and returns the parsed
/// model only when an entry with the same <c>Provider</c> AND <c>Id</c> is present in
/// <c>available</c>; otherwise <c>null</c>. Membership is the reachability/fallback guarantee.
///
/// The method (and the types it touches — <see cref="ChatModel"/>, <see cref="AiProvider"/>) is
/// reachable from the test assembly via <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>.
/// These methods are <c>public</c> rather than <c>internal</c> because their signatures expose no
/// less-accessible (internal) types — unlike the OSKind-based OllamaInstaller tests.
/// </summary>
public class ResolveModelOverrideTests
{
    private static readonly ChatModel OllamaLlama3 = new(AiProvider.Ollama, "llama3");
    private static readonly ChatModel OpenAiGpt4o = new(AiProvider.OpenAI, "gpt-4o");

    // --- Empty / null setting --------------------------------------------------------------------
    // Whatever the available list contains, an absent setting never resolves.

    [Theory]
    [InlineData(null)]   // never configured
    [InlineData("")]     // empty
    [InlineData("   ")]  // whitespace-only
    [InlineData("\t")]   // whitespace-only (tab)
    public void ResolveModelOverride_NullOrWhitespaceSetting_ReturnsNull(string? setting)
    {
        // Arrange: a non-empty list, so a null result is due to the setting (not an empty pool).
        var available = new List<ChatModel> { OllamaLlama3, OpenAiGpt4o };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride(setting, available);

        // Assert
        Assert.Null(result);
    }

    // --- Present in the available list -----------------------------------------------------------

    [Fact]
    public void ResolveModelOverride_OllamaModelPresent_ReturnsParsedModel()
    {
        // Arrange
        var available = new List<ChatModel> { OllamaLlama3, OpenAiGpt4o };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("Ollama:llama3", available);

        // Assert: matched by Provider + Id.
        Assert.NotNull(result);
        Assert.Equal(AiProvider.Ollama, result!.Provider);
        Assert.Equal("llama3", result.Id);
        // NOTE: ResolveModelOverride returns the *parsed* (freshly constructed) ChatModel, NOT the
        // instance from `available`, so Assert.Same against the list entry would FAIL. ChatModel is a
        // record (value equality), so the returned value still equals the list entry by value.
        Assert.Equal(OllamaLlama3, result);
        Assert.NotSame(OllamaLlama3, result);
    }

    [Fact]
    public void ResolveModelOverride_OpenAiModelPresent_ReturnsParsedModel()
    {
        // Arrange: proves provider parsing of the "{provider}:{id}" prefix beyond Ollama.
        var available = new List<ChatModel> { OllamaLlama3, OpenAiGpt4o };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("OpenAI:gpt-4o", available);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AiProvider.OpenAI, result!.Provider);
        Assert.Equal("gpt-4o", result.Id);
        Assert.Equal(OpenAiGpt4o, result);
    }

    // --- Absent from the available list ----------------------------------------------------------

    [Fact]
    public void ResolveModelOverride_PresentSettingButEmptyAvailable_ReturnsNull()
    {
        // Arrange: nothing is reachable.
        var available = new List<ChatModel>();

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("Ollama:llama3", available);

        // Assert: parse succeeds, but membership fails -> null (reachability/fallback guarantee).
        Assert.Null(result);
    }

    [Fact]
    public void ResolveModelOverride_SettingNotInNonEmptyAvailable_ReturnsNull()
    {
        // Arrange: the list has other entries, but not the requested one.
        var available = new List<ChatModel>
        {
            new(AiProvider.Ollama, "mistral"),
            new(AiProvider.Gemini, "gemini-1.5-pro"),
        };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("Ollama:llama3", available);

        // Assert
        Assert.Null(result);
    }

    // --- Legacy bare model name (no provider prefix) ---------------------------------------------
    // ParseSavedModel treats a bare value as an Ollama id.

    [Fact]
    public void ResolveModelOverride_LegacyBareName_Present_ResolvesAsOllama()
    {
        // Arrange
        var available = new List<ChatModel> { OllamaLlama3 };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("llama3", available);

        // Assert: a colon-less value parses as Ollama:llama3 and matches the list entry.
        Assert.NotNull(result);
        Assert.Equal(AiProvider.Ollama, result!.Provider);
        Assert.Equal("llama3", result.Id);
    }

    [Fact]
    public void ResolveModelOverride_LegacyBareName_Absent_ReturnsNull()
    {
        // Arrange: the bare name parses as Ollama:llama3, which isn't present.
        var available = new List<ChatModel> { OpenAiGpt4o };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("llama3", available);

        // Assert
        Assert.Null(result);
    }

    // --- Garbage / unknown provider prefix -------------------------------------------------------

    [Fact]
    public void ResolveModelOverride_UnknownProviderPrefix_FallsThroughToBareOllamaId_ReturnsNull()
    {
        // Arrange: "NotAProvider" is not a valid AiProvider, so ParseSavedModel does NOT split on the
        // colon; it falls through to the legacy branch and treats the WHOLE string as a bare Ollama
        // id -> ChatModel(Ollama, "NotAProvider:x"). That id isn't in `available`, so result is null.
        var available = new List<ChatModel> { OllamaLlama3, OpenAiGpt4o };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("NotAProvider:x", available);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveModelOverride_UnknownProviderPrefix_WholeStringIsOllamaId_ResolvesWhenPresent()
    {
        // Arrange: prove the fall-through really keeps the entire raw string as the Ollama id (it does
        // NOT strip the "NotAProvider:" prefix). Put exactly that ChatModel in the list and it matches.
        var fallback = new ChatModel(AiProvider.Ollama, "NotAProvider:x");
        var available = new List<ChatModel> { fallback };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("NotAProvider:x", available);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AiProvider.Ollama, result!.Provider);
        Assert.Equal("NotAProvider:x", result.Id);
    }

    // --- Provider-sensitivity: membership is Provider AND Id, not Id alone -----------------------

    [Fact]
    public void ResolveModelOverride_SameIdDifferentProvider_DoesNotResolve()
    {
        // Arrange: the only available entry shares the Id "llama3" but under the Ollama provider; the
        // setting asks for OpenAI:llama3. Membership compares Provider AND Id, so this must NOT match.
        var available = new List<ChatModel> { OllamaLlama3 };

        // Act
        var result = MainWindowViewModel.ResolveModelOverride("OpenAI:llama3", available);

        // Assert
        Assert.Null(result);
    }
}
