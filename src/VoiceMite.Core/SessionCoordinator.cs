namespace VoiceMite.Core;

/// <summary>Owns a single generation; cancellation invalidates state before contacting an asynchronous provider.</summary>
public sealed class SessionCoordinator : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private long generation;
    public SessionState State { get; private set; } = SessionState.Idle;
    public string Transcript { get; private set; } = "";
    public RewriteResult? Result { get; private set; }
    public ServiceErrorCode? ErrorCode { get; private set; }
    public event Action? Changed;

    public void BeginRecording() => Reset(SessionState.Recording);
    public void Cancel() => Reset(SessionState.Cancelled);
    public void Clear() => Reset(SessionState.Idle);
    public void Fail(ServiceErrorCode code)
    {
        lock (gate)
        {
            ++generation; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            State = SessionState.Error; ErrorCode = code; Result = null;
        }
        Changed?.Invoke();
    }

    public async Task TranscribeAndFormatAsync(byte[] wav, AppSettings settings, ISpeechService speech, IRewriteService rewrite, LanguagePreferences? prefs = null)
    {
        var run = Start(SessionState.Transcribing, false);
        try
        {
            var transcript = await speech.TranscribeAsync(wav, settings.SpeechLanguage, null, run.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(transcript)) throw new ServiceException(ServiceErrorCode.EmptyTranscript);
            if (transcript.Length > 16000) throw new ServiceException(ServiceErrorCode.InputTooLong);
            if (!Update(run.Id, () => Transcript = transcript)) return;
            await FormatCoreAsync(run.Id, transcript, settings, rewrite, settings.Mode, prefs, run.Token).ConfigureAwait(false);
        }
        catch (Exception error) { CompleteError(run.Id, error); }
    }

    public async Task FormatAsync(string transcript, AppSettings settings, IRewriteService rewrite, InputMode? mode = null, LanguagePreferences? prefs = null)
    {
        var run = Start(SessionState.Formatting, true);
        try
        {
            if (string.IsNullOrWhiteSpace(transcript)) throw new ServiceException(ServiceErrorCode.EmptyTranscript);
            if (transcript.Length > 16000) throw new ServiceException(ServiceErrorCode.InputTooLong);
            if (!Update(run.Id, () => Transcript = transcript)) return;
            await FormatCoreAsync(run.Id, transcript, settings, rewrite, mode ?? settings.Mode, prefs, run.Token).ConfigureAwait(false);
        }
        catch (Exception error) { CompleteError(run.Id, error); }
    }

    private async Task FormatCoreAsync(long id, string transcript, AppSettings settings, IRewriteService rewrite, InputMode mode, LanguagePreferences? prefs, CancellationToken token)
    {
        RewriteResult result;
        if (settings.TranscribeOnly || mode == InputMode.Verbatim)
            result = new RewriteResult(transcript, InputMode.Verbatim);
        else
        {
            if (!Update(id, () => State = SessionState.Formatting)) return;
            result = await rewrite.RewriteAsync(new(transcript, mode, settings.OutputLanguage, settings.UiLanguage, settings.LearnPreferences ? prefs : null), token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        Update(id, () => { Result = result; State = result.Question is null ? SessionState.Ready : SessionState.Clarifying; });
    }

    private (long Id, CancellationToken Token) Start(SessionState state, bool keepTranscript)
    {
        long id; CancellationToken token;
        lock (gate)
        {
            ++generation; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new();
            id = generation; token = cancellation.Token;
            State = state; Result = null; ErrorCode = null;
            if (!keepTranscript) Transcript = "";
        }
        Changed?.Invoke();
        return (id, token);
    }

    private void Reset(SessionState state)
    {
        lock (gate)
        {
            ++generation; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            State = state; Transcript = ""; Result = null; ErrorCode = null;
        }
        Changed?.Invoke();
    }

    private bool Update(long id, Action action)
    {
        lock (gate)
        {
            if (id != generation || cancellation?.IsCancellationRequested != false) return false;
            action();
        }
        Changed?.Invoke();
        return true;
    }

    private void CompleteError(long id, Exception error) => Update(id, () =>
    {
        Result = null;
        if (error is OperationCanceledException) { State = SessionState.Cancelled; ErrorCode = null; }
        else { State = SessionState.Error; ErrorCode = error is ServiceException service ? service.Code : ServiceErrorCode.Unknown; }
    });

    public void Dispose() { Reset(SessionState.Cancelled); GC.SuppressFinalize(this); }
}
