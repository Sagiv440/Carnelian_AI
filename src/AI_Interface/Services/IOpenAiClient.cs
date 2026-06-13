namespace AI_Interface.Services;

/// <summary>Marker interface so the OpenAI client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface IOpenAiClient : IChatClient
{
}

/// <summary>Marker interface so the Gemini client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface IGeminiClient : IChatClient
{
}

/// <summary>Marker interface so the Anthropic client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface IAnthropicClient : IChatClient
{
}

/// <summary>Marker interface so the DeepSeek client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface IDeepSeekClient : IChatClient
{
}

/// <summary>Marker interface so the Nvidia (NIM) client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface INvidiaClient : IChatClient
{
}

/// <summary>Marker interface so the Mistral AI client gets its own typed <see cref="System.Net.Http.HttpClient"/>.</summary>
public interface IMistralClient : IChatClient
{
}
