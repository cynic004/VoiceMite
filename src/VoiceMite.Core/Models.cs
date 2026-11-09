namespace VoiceMite.Core;

public enum InputMode { Auto, Verbatim, Search, Chat, Prompt }
public enum SessionState { Idle, Recording, Transcribing, Formatting, Clarifying, Ready, Inserting, Error, Cancelled }
public enum ServiceKind { OpenAi, Demo }
public sealed record ServiceConfig
{
    public ServiceKind Kind { get; set; } = ServiceKind.Demo;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}
public sealed record AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string UiLanguage { get; set; } = "zh-CN";
    public string SpeechLanguage { get; set; } = "zh";
    public string OutputLanguage { get; set; } = "same";
    public InputMode Mode { get; set; } = InputMode.Auto;
    public ServiceConfig Stt { get; set; } = new() { Model = "whisper-1" };
    public ServiceConfig Llm { get; set; } = new() { Model = "gpt-4o-mini" };
    public int MicrophoneId { get; set; } = -1;
    public bool TranscribeOnly { get; set; }
    public bool LiveSummary { get; set; }
    public bool LearnPreferences { get; set; }
    public bool ReduceMotion { get; set; }
    public bool HighContrast { get; set; }
    public bool PrivacyAccepted { get; set; }
    public double FloatLeft { get; set; } = 80;
    public double FloatTop { get; set; } = 160;
}
public sealed record LanguagePreferences
{
    public string Script { get; set; } = "";
    public string Punctuation { get; set; } = "";
    public string Length { get; set; } = "";
    public string Tone { get; set; } = "";
    public string Terms { get; set; } = "";
}
public sealed record TranscriptUpdate(string Text, bool IsFinal);
public sealed record RewriteRequest(string Transcript, InputMode Mode, string OutputLanguage, string UiLanguage, LanguagePreferences? Preferences = null);
public sealed record RewriteResult(string Text, InputMode Mode, string Summary = "", string? Question = null, IReadOnlyList<InputMode>? Choices = null);
public interface ISpeechService
{
    bool SupportsLiveTranscription { get; }
    Task<string> TranscribeAsync(byte[] wav, string language, IProgress<TranscriptUpdate>? progress, CancellationToken cancellationToken);
    Task TestAsync(CancellationToken cancellationToken);
}
public interface IRewriteService
{
    Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken);
    Task TestAsync(CancellationToken cancellationToken);
}
