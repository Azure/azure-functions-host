// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using System.Linq;

namespace Microsoft.Azure.Functions.Analyzers
{
    internal static class MethodSymbolExtensions
    {
        public static bool IsFunction(this IMethodSymbol symbol, Compilation compilation)
        {
            var attributes = symbol.GetAttributes();

            if (attributes.IsEmpty)
            {
                return false;
            }

            var attributeType = compilation.GetTypeByMetadataName(Constants.Types.FunctionNameAttribute);

            return attributes.Any(a => attributeType.IsAssignableFrom(a.AttributeClass, true));
        }
    }
}
