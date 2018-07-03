// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class CachedCSharpCompilation : ICSharpCompilation
    {
        private readonly ICSharpCompilation _innerCompilation;
        private readonly FunctionMetadata _functionMetadata;
        private readonly ILogger _logger;
        private static readonly ConcurrentDictionary<string, Task<Assembly>> _cachedCompilations = new ConcurrentDictionary<string, Task<Assembly>>();

        public CachedCSharpCompilation(ICSharpCompilation compilation, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory)
        {
            _innerCompilation = compilation;
            _functionMetadata = functionMetadata;
            _logger = loggerFactory?.CreateLogger(LogCategories.Startup) ?? NullLogger.Instance;
        }

        Compilation ICSharpCompilation.Compilation => _innerCompilation.Compilation;

        public Task<Assembly> EmitAsync(CancellationToken cancellationToken)
        {
            int hash = GeCompilationHash(_innerCompilation.Compilation);

            string key = $"{_functionMetadata.Name}::{hash}";

            _logger.LogInformation($"Attempting to retrieve cached compilation for function {_functionMetadata.Name}");
            var compilationResults = _cachedCompilations.GetOrAdd(key, k =>
            {
                _logger.LogInformation($"Compilation cache miss for function {_functionMetadata.Name} (hash: {hash}).");
                return _innerCompilation.EmitAsync(cancellationToken);
            });

            _logger.LogInformation($"Returning compilation results for function {_functionMetadata.Name} (cache hash: {hash})");

            return compilationResults;
        }

        internal static int GeCompilationHash(Compilation compilation)
        {
            int hash = compilation.SyntaxTrees.Aggregate(0, (i, t) => HashCode.Combine(t.ToString(), i));
            hash = HashCode.Combine(hash, compilation.References.Count());
            hash = compilation.References
                .Where(m => File.Exists(m.Display))
                .Aggregate(hash, (a, r) => HashCode.Combine(File.GetLastWriteTimeUtc(r.Display), a));

            return hash;
        }

        public ImmutableArray<Diagnostic> GetDiagnostics()
        {
            return _innerCompilation.GetDiagnostics();
        }

        public FunctionSignature GetEntryPointSignature(IFunctionEntryPointResolver entryPointResolver)
        {
            return _innerCompilation.GetEntryPointSignature(entryPointResolver);
        }

        async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken) => await EmitAsync(cancellationToken);
    }
}
