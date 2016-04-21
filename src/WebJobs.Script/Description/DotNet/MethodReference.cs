// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class MethodReference<T> : IMethodReference
    {
        public MethodReference(string name, bool isPublic, T value)
        {
            Name = name;
            IsPublic = isPublic;
            Value = value;
        }

        public string Name { get; set; }

        public bool IsPublic { get; set; }

        public T Value { get; set; }
    }
}
