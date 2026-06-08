namespace AI_Interface.Models;

/// <summary>
/// Which text-to-speech engine the app uses to read replies aloud.
/// Mirrors the multi-provider shape of chat (<see cref="AiProvider"/>) so more engines
/// (e.g. OpenAI TTS, OS built-in) can be added behind <see cref="Services.ISpeechService"/> later.
/// </summary>
public enum SpeechProvider
{
    /// <summary>Voice output disabled.</summary>
    None,

    /// <summary>Local, offline neural TTS via the Piper binary.</summary>
    Piper
}
