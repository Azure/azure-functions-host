// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionParameter : IEquatable<FunctionParameter>
    {
        public FunctionParameter(string name, string typeName, bool isOptional, RefKind refkind)
        {
            Name = name;
            TypeName = typeName;
            IsOptional = isOptional;
            RefKind = refkind;
        }

        public string Name { get; }

        public string TypeName { get; }

        public bool IsOptional { get; }

        public RefKind RefKind { get; }

        public bool Equals(FunctionParameter other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.RefKind == other.RefKind &&
                string.Compare(this.Name, other.Name, StringComparison.Ordinal) == 0 &&
                string.Compare(this.TypeName, other.TypeName, StringComparison.Ordinal) == 0 &&
                this.IsOptional == other.IsOptional;
        }

        public override bool Equals(object obj) => Equals(obj as FunctionParameter);

        public override int GetHashCode() => RefKind.GetHashCode() ^ Name.GetHashCode() ^ TypeName.GetHashCode() ^ IsOptional.GetHashCode();
    }
}
