﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface IFunctionMetadataResolver
    {
        ScriptOptions CreateScriptOptions();

        IReadOnlyCollection<string> GetCompilationReferences();

        Assembly ResolveAssembly(string assemblyName);

        ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties);

        Task<PackageRestoreResult> RestorePackagesAsync();

        bool RequiresPackageRestore(FunctionMetadata metadata);

        bool TryGetPackageReference(string referenceName, out PackageReference package);

        bool TryResolvePrivateAssembly(string name, out string assemblyPath);
    }
}