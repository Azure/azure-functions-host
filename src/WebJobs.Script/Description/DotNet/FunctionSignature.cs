// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides function identity validation and identification.
    /// </summary>
    [CLSCompliant(false)]
    public sealed class FunctionSignature : IEquatable<FunctionSignature>
    {
        private readonly ImmutableArray<IParameterSymbol> _parameters;
        private readonly bool _hasLocalTypeReference;
        private readonly string _parentTypeName;
        private readonly string _methodName;

        public FunctionSignature(string parentTypeName, string methodName, ImmutableArray<IParameterSymbol> parameters, bool hasLocalTypeReference)
        {
            _parameters = parameters;
            _hasLocalTypeReference = hasLocalTypeReference;
            _parentTypeName = parentTypeName;
            _methodName = methodName;
        }

        /// <summary>
        /// Returns true if the function uses locally defined types (i.e. types defined in the function assembly) in its parameters;
        /// otherwise, false.
        /// </summary>
        public bool HasLocalTypeReference
        {
            get
            {
                return _hasLocalTypeReference;
            }
        }

        public ImmutableArray<IParameterSymbol> Parameters
        {
            get
            {
                return _parameters;
            }
        }

        public string ParentTypeName
        {
            get
            {
                return _parentTypeName;
            }
        }

        public string MethodName
        {
            get
            {
                return _methodName;
            }
        }

        private static bool AreParametersEquivalent(IParameterSymbol param1, IParameterSymbol param2)
        {
            if (ReferenceEquals(param1, param2))
            {
                return true;
            }

            if (param1 == null || param2 == null)
            {
                return false;
            }

            return param1.RefKind == param2.RefKind &&
                string.Compare(param1.Name, param2.Name, StringComparison.Ordinal) == 0 &&
                string.Compare(GetFullTypeName(param1.Type), GetFullTypeName(param2.Type), StringComparison.Ordinal) == 0 &&
                param1.IsOptional == param2.IsOptional;
        }

        private static string GetFullTypeName(ITypeSymbol type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}, {2}", type.ContainingNamespace.MetadataName, type.MetadataName, type.ContainingAssembly.ToDisplayString());
        }

        public bool Equals(FunctionSignature other)
        {
            if (other == null)
            {
                return false;
            }

            if (_parameters.Count() != other._parameters.Count())
            {
                return false;
            }

            return _parameters.Zip(other._parameters, (a, b) => AreParametersEquivalent(a, b)).All(r => r);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FunctionSignature);
        }

        public override int GetHashCode()
        {
            return string.Join("<>", _parameters.Select(p => GetParameterIdentityString(p)))
                .GetHashCode();
        }

        private static string GetParameterIdentityString(IParameterSymbol parameterSymbol)
        {
            return string.Join("::", parameterSymbol.RefKind, parameterSymbol.Name,
                GetFullTypeName(parameterSymbol.Type), parameterSymbol.IsOptional);
        }
    }
}
