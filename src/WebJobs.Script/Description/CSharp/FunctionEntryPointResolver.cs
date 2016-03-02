using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using static System.FormattableString;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides entry point resolution for managed functions.
    /// </summary>
    public class FunctionEntryPointResolver : IFunctionEntryPointResolver
    {
        /// <summary>
        /// Resolves the method that should be used as the entry point.
        /// </summary>
        /// <param name="declaredMethods">The list of methods to evaluate.</param>
        /// <returns>The best match from the provided method list.</returns>
        public MethodInfo GetFunctionEntryPoint(IList<MethodInfo> declaredMethods)
        {
            if (declaredMethods == null)
            {
                throw new ArgumentNullException(nameof(declaredMethods));
            }

            if (declaredMethods.Count == 1)
            {
                return declaredMethods[0];
            }
            
            var runMethods = declaredMethods
                .Where(m => m.IsPublic && string.Compare(m.Name, "run", StringComparison.OrdinalIgnoreCase) == 0)
                .ToList();

            if (runMethods.Count == 1)
            {
                return runMethods[0];
            }

            if (runMethods.Count > 1)
            {
                throw CreateCompilationException("AF002", "Ambiguous function entry points. Multiple 'Run' methods.",
                    Invariant($"Multiple methods named 'Run'. Consider renaming methods."));
            }

            throw CreateCompilationException("AF001", "Missing function entry point", 
                Invariant($"Your function must contain a single method, or a single public entry point method named 'Run'."));
        }

        private static CompilationErrorException CreateCompilationException(string code, string title, string messageFormat)
        {
            var descriptor = new DiagnosticDescriptor(code, title, messageFormat, "AzureFunctions", DiagnosticSeverity.Error, true);

            return new CompilationErrorException(title, ImmutableArray.Create(Diagnostic.Create(descriptor, Location.None)));
        }
    }
}
