using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionEntryPointResolver : IFunctionEntryPointResolver
    {
        public MethodInfo GetFunctionEntryPoint(IList<MethodInfo> declaredMethods)
        {
            if (declaredMethods.Count == 1)
            {
                return declaredMethods[0];
            }

            var attributedMethods = new List<MethodInfo>();
            var runMethods = new List<MethodInfo>();

            foreach (var methodInfo in declaredMethods)
            {
                if (methodInfo.GetCustomAttribute<FunctionEntryPointAttribute>() != null)
                {
                    attributedMethods.Add(methodInfo);
                }

                if (string.Compare(methodInfo.Name, "run", true) == 0)
                {
                    runMethods.Add(methodInfo);
                }
            }

            if (attributedMethods.Count == 1)
            {
                return attributedMethods[0];
            }

            if (attributedMethods.Count > 1)
            {
                throw CreateCompilationException("AF002", "Multiple function entry points", $"Only one method may be decorated with the {nameof(FunctionEntryPointAttribute)}.");
            }

            if (runMethods.Count == 1)
            {
                return runMethods[0];
            }

            if (attributedMethods.Count > 1)
            {
                throw CreateCompilationException("AF003", "Ambiguous function entry points. Multiple 'Run' methods.", 
                    $"Multiple methods named 'Run'. Consider renaming methods or apply the {nameof(FunctionEntryPointAttribute)} to the entry point.");
            }

            throw CreateCompilationException("AF001", "Missing function entry point", 
                $"Your function must contain an entry point method named 'Run' or a method decorated with the {nameof(FunctionEntryPointAttribute)}.");
        }

        private CompilationErrorException CreateCompilationException(string code, string title, string messageFormat)
        {
            var descriptor = new DiagnosticDescriptor(code, title, messageFormat, "AzureFunctions", DiagnosticSeverity.Error, true);

            return new CompilationErrorException(title, ImmutableArray.Create(Diagnostic.Create(descriptor, Location.None)));
        }
    }
}
