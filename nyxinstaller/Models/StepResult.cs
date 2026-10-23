namespace NyxInstaller.Models;

/// <summary>
/// Result of a single install step.
/// </summary>
public sealed record StepResult
{
    private StepResult(bool success, bool cancelled, string? message, Exception? exception)
    {
        Success = success;
        Cancelled = cancelled;
        Message = message;
        Exception = exception;
    }

    public bool Success { get; }

    public bool Cancelled { get; }

    public string? Message { get; }

    public Exception? Exception { get; }

    public static StepResult Ok() => new(true, false, null, null);

    public static StepResult Cancel() => new(false, true, null, null);

    public static StepResult Fail(string message, Exception? exception = null) =>
        new(false, false, message, exception);
}
