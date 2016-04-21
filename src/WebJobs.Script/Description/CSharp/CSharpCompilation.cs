// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class CSharpCompilation : ICompilation
    {
        private Compilation _compilation;

        public CSharpCompilation(Compilation compilation)
        {
            _compilation = compilation;
        }
        public ImmutableArray<Diagnostic> GetDiagnostics()
        {
            return _compilation.GetDiagnostics();
        }
        public Assembly Emit()
        {
            using (MemoryStream assemblyStream = new MemoryStream())
            {
                using (MemoryStream pdbStream = new MemoryStream())
                {
                    var result = _compilation.Emit(assemblyStream, pdbStream);

                    if (!result.Success)
                    {
                        throw new CompilationErrorException("Script compilation failed.", result.Diagnostics);
                    }

                    Assembly assembly = Assembly.Load(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                    return assembly;
                }
            }
        }

        public DotNetFunctionSignature FindEntryPoint(IFunctionEntryPointResolver entryPointResolver)
        {
            IEnumerable<MethodReference<IMethodSymbol>> methods =
                _compilation.ScriptClass
                    .GetMembers()
                  .OfType<IMethodSymbol>()
                  .Select(m => new MethodReference<IMethodSymbol>(m.Name, m.DeclaredAccessibility == Accessibility.Public, m));

            IMethodSymbol entryPointReference = entryPointResolver.GetFunctionEntryPoint(methods).Value;

            var signature = new DotNetFunctionSignature(entryPointReference.Parameters.Select(x => x.Name).ToImmutableArray(), entryPointReference.Parameters);

            signature.HasLocalTypeReference = entryPointReference.Parameters.Any(p => IsOrUsesAssemblyType(p.Type, entryPointReference.ContainingAssembly));

            return signature;
        }
        private static bool IsOrUsesAssemblyType(ITypeSymbol typeSymbol, IAssemblySymbol assemblySymbol)
        {
            if (typeSymbol.ContainingAssembly == assemblySymbol)
            {
                return true;
            }

            INamedTypeSymbol namedTypeSymbol = typeSymbol as INamedTypeSymbol;
            return namedTypeSymbol != null && namedTypeSymbol.IsGenericType
                && namedTypeSymbol.TypeArguments.Any(t => IsOrUsesAssemblyType(t, assemblySymbol));
        }
    }
}
