// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides function identity validation and identification.
    /// </summary>
    public sealed class FunctionSignature : IEquatable<FunctionSignature>
    {
        private readonly ImmutableArray<FunctionParameter> _parameters;
        private readonly bool _hasLocalTypeReference;
        private readonly string _parentTypeName;
        private readonly string _methodName;
        private readonly string _returnTypeName;

        public FunctionSignature(string parentTypeName, string methodName, ImmutableArray<FunctionParameter> parameters, string returnTypeName, bool hasLocalTypeReference)
        {
            _parameters = parameters;
            _hasLocalTypeReference = hasLocalTypeReference;
            _parentTypeName = parentTypeName;
            _returnTypeName = returnTypeName;
            _methodName = methodName;
        }

        /// <summary>
        /// Gets a value indicating whether the function uses locally defined types (i.e. types defined in the function assembly) in its parameters.
        /// </summary>
        public bool HasLocalTypeReference => _hasLocalTypeReference;

        public ImmutableArray<FunctionParameter> Parameters => _parameters;

        public string ParentTypeName => _parentTypeName;

        public string MethodName => _methodName;

        public string ReturnTypeName => _returnTypeName;

        public MethodInfo GetMethod(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            return assembly.DefinedTypes
                .FirstOrDefault(t => string.Compare(t.FullName, ParentTypeName, StringComparison.Ordinal) == 0)
                ?.GetMethod(MethodName);
        }

        public bool Equals(FunctionSignature other)
        {
            if (other == null)
            {
                return false;
            }

            if (!string.Equals(ParentTypeName, other.ParentTypeName) ||
                !string.Equals(MethodName, other.MethodName) ||
                !string.Equals(ReturnTypeName, other.ReturnTypeName) ||
                HasLocalTypeReference != other.HasLocalTypeReference ||
                _parameters.Count() != other._parameters.Count())
            {
                return false;
            }

            return _parameters.Zip(other._parameters, (a, b) => a.Equals(b)).All(r => r);
        }

        public override bool Equals(object obj) => Equals(obj as FunctionSignature);

        public override int GetHashCode() => GetPropertyHashCode(ParentTypeName) ^
            GetPropertyHashCode(MethodName) ^
            GetPropertyHashCode(ReturnTypeName) ^
            HasLocalTypeReference.GetHashCode() ^
            _parameters.Aggregate(0, (a, p) => a ^ p.GetHashCode());

        private static int GetPropertyHashCode(string value) => value?.GetHashCode() ?? 0;
    }
}
