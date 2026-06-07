using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>Pairs a <see cref="SearchProvider"/> with its friendly label for the provider selector.</summary>
public sealed record SearchProviderOption(SearchProvider Provider, string Label)
{
    public override string ToString() => Label;
}
