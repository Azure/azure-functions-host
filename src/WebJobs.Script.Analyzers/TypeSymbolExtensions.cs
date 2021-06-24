using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.Functions.Analyzers
{
    internal static class TypeSymbolExtensions
    {
        internal static bool IsAssignableFrom(this ITypeSymbol targetType, ITypeSymbol sourceType, bool exactMatch = false)
        {
            if (targetType != null)
            {
                while (sourceType != null)
                {
                    if (sourceType.Equals(targetType, SymbolEqualityComparer.Default))
                    {
                        return true;
                    }

                    if (exactMatch)
                    {
                        return false;
                    }

                    if (targetType.TypeKind == TypeKind.Interface)
                    {
                        return sourceType.AllInterfaces.Any(i => i.Equals(targetType, SymbolEqualityComparer.Default));
                    }

                    sourceType = sourceType.BaseType;
                }
            }

            return false;
        }
    }
}
