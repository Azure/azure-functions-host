// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawAssemblyCompilation : IDotNetCompilation
    {
        private static readonly Regex _entryPointRegex = new Regex("^(?<typename>.*)\\.(?<methodname>\\S*)$", RegexOptions.Compiled);
        private readonly string _assemblyFilePath;
        private readonly string _entryPointName;

        public RawAssemblyCompilation(string assemblyFilePath, string entryPointName)
        {
            _assemblyFilePath = assemblyFilePath;
            _entryPointName = entryPointName;
        }

        async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken)
             => await EmitAsync(cancellationToken);

        public Task<DotNetCompilationResult> EmitAsync(CancellationToken cancellationToken)
        {
            DotNetCompilationResult result = null;
            if (DependencyHelper.TryGetAssemblyReference(_assemblyFilePath, out string assemblyName))
            {
                result = DotNetCompilationResult.FromAssemblyName(assemblyName);
            }
            else
            {
                result = DotNetCompilationResult.FromPath(_assemblyFilePath);
            }

            return Task.FromResult(result);
        }

        public ImmutableArray<Diagnostic> GetDiagnostics() => ImmutableArray<Diagnostic>.Empty;

        public FunctionSignature GetEntryPointSignature(IFunctionEntryPointResolver entryPointResolver, Assembly functionAssembly)
        {
            var entryPointMatch = _entryPointRegex.Match(_entryPointName);
            if (!entryPointMatch.Success)
            {
                throw new InvalidOperationException("Invalid entry point configuration. The function entry point must be defined in the format <fulltypename>.<methodname>");
            }

            string typeName = entryPointMatch.Groups["typename"].Value;
            string methodName = entryPointMatch.Groups["methodname"].Value;

            Type functionType = functionAssembly.GetType(typeName);
            if (functionType == null)
            {
                throw new InvalidOperationException($"The function type name '{typeName}' is invalid.");
            }

            // To preserve legacy behavior, we attempt to bind to public static methods first, then fallback to instance
            // methods if a match is not found.
            MethodInfo method = functionType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
                ?? functionType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public);
            if (method == null)
            {
                throw new InvalidOperationException($"The method '{methodName}' cannot be found.");
            }

            var functionParameters = method.GetParameters().Select(p => new FunctionParameter(p.Name, p.ParameterType.FullName, p.IsOptional, GetParameterRefKind(p)));

            return new FunctionSignature(method.ReflectedType.FullName, method.Name, ImmutableArray.CreateRange(functionParameters.ToArray()), method.ReturnType.Name, hasLocalTypeReference: false);
        }

        private static RefKind GetParameterRefKind(ParameterInfo parameter)
        {
            if (parameter.IsOut)
            {
                return RefKind.Out;
            }

            return RefKind.None;
        }
    }
}
