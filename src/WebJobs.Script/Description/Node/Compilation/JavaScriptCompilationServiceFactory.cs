// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class JavaScriptCompilationServiceFactory : ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata>
    {
        private static readonly ImmutableArray<ScriptType> SupportedScriptTypes = new[] { ScriptType.Javascript, ScriptType.TypeScript }.ToImmutableArray();

        ImmutableArray<ScriptType> ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata>.SupportedScriptTypes => SupportedScriptTypes;

        public ICompilationService<IJavaScriptCompilation> CreateService(ScriptType scriptType, FunctionMetadata metadata)
        {
            switch (scriptType)
            {
                case ScriptType.Javascript:
                    return new RawJavaScriptCompilationService(metadata);
                case ScriptType.TypeScript:
                    return new TypeScriptCompilationService();
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "The script type {0} is not supported by the {1}", scriptType, typeof(JavaScriptCompilationServiceFactory).Name));
            }
        }
    }
}
