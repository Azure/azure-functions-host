// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class DotNetCompilationResult
    {
        public static DotNetCompilationResult FromBytes(byte[] assemblyBytes, byte[] pdbBytes = null)
            => new BinaryCompilationResult(assemblyBytes, pdbBytes);

        public static DotNetCompilationResult FromStream(Stream assemblyStream, Stream pdbStream = null)
            => new BinaryCompilationResult(assemblyStream, pdbStream);

        public static DotNetCompilationResult FromPath(string assemblyPath)
            => new PathCompilationResult(assemblyPath);

        public static DotNetCompilationResult FromAssemblyName(string assemblyName)
            => new AssemblyReferenceCompilationResult(assemblyName);

        public abstract Assembly Load(FunctionMetadata metadata, IFunctionMetadataResolver metadataResolver, ILogger logger);

        private sealed class BinaryCompilationResult : DotNetCompilationResult
        {
            public BinaryCompilationResult(Stream assemblyBytes)
                : this(assemblyBytes, null)
            {
            }

            public BinaryCompilationResult(Stream assemblyStream, Stream pdbStream)
            {
                AssemblyStream = assemblyStream ?? throw new ArgumentNullException(nameof(assemblyStream));
                PdbStream = pdbStream;
            }

            public BinaryCompilationResult(byte[] assemblyBytes, byte[] pdbBytes)
            {
                if (assemblyBytes == null)
                {
                    throw new ArgumentNullException(nameof(assemblyBytes));
                }

                AssemblyStream = new MemoryStream(assemblyBytes);

                if (pdbBytes != null)
                {
                    PdbStream = new MemoryStream(pdbBytes);
                }
            }

            public Stream AssemblyStream { get; }

            public Stream PdbStream { get; }

            public override Assembly Load(FunctionMetadata metadata, IFunctionMetadataResolver metadataResolver, ILogger logger)
            {
                var context = new DynamicFunctionAssemblyLoadContext(metadata, metadataResolver, logger);

                AssemblyStream.Position = 0;

                if (PdbStream != null)
                {
                    PdbStream.Position = 0;
                }

                return context.LoadFromStream(AssemblyStream, PdbStream);
            }
        }

        private sealed class PathCompilationResult : DotNetCompilationResult
        {
            public PathCompilationResult(string assemblyPath)
            {
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    throw new ArgumentException("Invalid path string", nameof(assemblyPath));
                }

                AssemblyPath = assemblyPath;
            }

            public string AssemblyPath { get; }

            public override Assembly Load(FunctionMetadata metadata, IFunctionMetadataResolver metadataResolver, ILogger logger)
                => FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(AssemblyPath, true);
        }

        private sealed class AssemblyReferenceCompilationResult : DotNetCompilationResult
        {
            public AssemblyReferenceCompilationResult(string assemblyName)
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    throw new ArgumentException("Invalid assembly name string", nameof(assemblyName));
                }

                AssemblyName = assemblyName;
            }

            public string AssemblyName { get; }

            public override Assembly Load(FunctionMetadata metadata, IFunctionMetadataResolver metadataResolver, ILogger logger)
                => FunctionAssemblyLoadContext.Shared.LoadFromAssemblyName(new AssemblyName(AssemblyName));
        }
    }
}
