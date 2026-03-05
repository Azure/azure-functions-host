namespace WorkerModel.Contracts;

/// <summary>
/// Context for a worker, including its assigned application (if specialized).
/// </summary>
/// <param name="Application">The application this worker is assigned to (null if placeholder)</param>
/// <param name="WorkerId">Unique identifier for this worker instance</param>
/// <param name="Language">The worker language runtime (e.g., "dotnet-isolated")</param>
/// <param name="LanguageVersion">Version of the language runtime (e.g., "8.0")</param>
/// <param name="IsPlaceholder">True if the worker has not been specialized yet</param>
public record WorkerContext(
    ApplicationDefinition? Application,
    string WorkerId,
    string Language,
    string LanguageVersion,
    bool IsPlaceholder)
{
    /// <summary>
    /// Creates a placeholder worker context.
    /// </summary>
    public static WorkerContext CreatePlaceholder(string workerId, string language, string languageVersion)
        => new(null, workerId, language, languageVersion, IsPlaceholder: true);

    /// <summary>
    /// Creates a specialized worker context with an assigned application.
    /// </summary>
    public WorkerContext Specialize(ApplicationDefinition application)
        => this with { Application = application, IsPlaceholder = false };
}
