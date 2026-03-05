using System.Collections.Concurrent;
using WorkerModel.ScaleController.Models;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// In-memory storage for metadata (replaces Cosmos DB).
/// Blob storage is handled by Azure Storage emulator.
/// Data is lost on restart - suitable for development only.
/// </summary>
public class InMemoryStore
{
    // Applications metadata
    public ConcurrentDictionary<string, ApplicationInfo> Applications { get; } = new();

    // Runtimes
    public ConcurrentDictionary<string, RuntimeInfo> Runtimes { get; } = new();

    // Workers
    public ConcurrentDictionary<string, WorkerInfo> Workers { get; } = new();
}
