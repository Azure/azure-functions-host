// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class NoOpFunctionMetadataResolver : IFunctionMetadataResolver
    {
        public ScriptOptions CreateScriptOptions()
        {
            return ScriptOptions.Default;
        }

        public IReadOnlyCollection<string> GetCompilationReferences()
        {
            return new ReadOnlyCollection<string>(new List<string>());
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
            return Task.FromResult(new PackageRestoreResult()
            {
                IsInitialInstall = false,
                ReferencesChanged = false
            });
        }

        public bool TryGetPackageReference(string referenceName, out PackageReference package)
        {
            package = null;
            return false;
        }
    }
}
