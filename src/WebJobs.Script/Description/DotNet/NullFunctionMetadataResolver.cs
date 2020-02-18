// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class NullFunctionMetadataResolver : IFunctionMetadataResolver
    {
        private readonly Lazy<Task<PackageRestoreResult>> _emptyPackageResult = new Lazy<Task<PackageRestoreResult>>(GetEmptyPackageResult);
        private static readonly Lazy<NullFunctionMetadataResolver> _instance = new Lazy<NullFunctionMetadataResolver>(() => new NullFunctionMetadataResolver());

        private NullFunctionMetadataResolver()
        {
        }

        public static NullFunctionMetadataResolver Instance => _instance.Value;

        public ScriptOptions CreateScriptOptions()
        {
            return ScriptOptions.Default;
        }

        public IReadOnlyCollection<string> GetCompilationReferences()
        {
            return Array.Empty<string>();
        }

        public bool RequiresPackageRestore(FunctionMetadata metadata)
        {
            return false;
        }

        public Assembly ResolveAssembly(AssemblyName assemblyName, FunctionAssemblyLoadContext targetContext)
        {
            return null;
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public Task<PackageRestoreResult> RestorePackagesAsync()
        {
            return _emptyPackageResult.Value;
        }

        public bool TryGetPackageReference(string referenceName, out PackageReference package)
        {
            package = null;
            return false;
        }

        private static Task<PackageRestoreResult> GetEmptyPackageResult()
        {
            return Task.FromResult(new PackageRestoreResult()
            {
                IsInitialInstall = false,
                ReferencesChanged = false
            });
        }
    }
}
