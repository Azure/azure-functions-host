namespace WorkerModel.Contracts;

/// <summary>
/// Defines a deployed function application.
/// </summary>
/// <param name="ApplicationId">Unique identifier for the app (e.g., "sample-app")</param>
/// <param name="MetadataVersion">Version of the app metadata/configuration</param>
/// <param name="CodeVersion">Version of the deployed code (e.g., "v1.0.0")</param>
/// <param name="ScriptRoot">Path where the function app is mounted</param>
public record ApplicationDefinition(
    string ApplicationId,
    string MetadataVersion,
    string CodeVersion,
    string ScriptRoot);
