// TEST DOUBLE ONLY. This executable deliberately does not reference VoiceMite.Windows.
// No UI Automation, native input, microphone, clipboard, network, or user-window access occurs.
namespace VoiceMite.Windows;

public sealed record NativeResult(bool Success, string Code = "");
public sealed record TargetCaptureResult(bool Success, string Code, string TargetDescription);

public sealed class SafeTargetService
{
    public bool HasTarget { get; private set; }
    public string TargetDescription => HasTarget ? "SIMULATED input field" : "";
    public TargetCaptureResult Capture()
    {
        HasTarget = true;
        return new(true, "", TargetDescription);
    }
    public NativeResult Poll() => new(HasTarget, HasTarget ? "" : "TargetMissing");
    public void Clear() => HasTarget = false;
    public Task<NativeResult> InsertAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = HasTarget;
        Clear();
        return Task.FromResult(new NativeResult(available, available ? "Inserted" : "TargetMissing"));
    }
}
