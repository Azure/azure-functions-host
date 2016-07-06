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
        private readonly ImmutableArray<FunctionParameter> _parameters;
        private readonly bool _hasLocalTypeReference;
        private readonly string _parentTypeName;
        private readonly string _methodName;

        public FunctionSignature(string parentTypeName, string methodName, ImmutableArray<FunctionParameter> parameters, bool hasLocalTypeReference)
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

        public ImmutableArray<FunctionParameter> Parameters
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

            return _parameters.Zip(other._parameters, (a, b) => a.Equals(b)).All(r => r);
        }

        public override bool Equals(object obj) => Equals(obj as FunctionSignature);

        public override int GetHashCode() => _parameters.Aggregate(0, (a, p) => a ^ p.GetHashCode());
    }
}
