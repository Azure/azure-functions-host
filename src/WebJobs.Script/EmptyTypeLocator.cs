// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script
{
    public class EmptyTypeLocator : ITypeLocator
    {
        private readonly ReadOnlyCollection<Type> _types;
        private static readonly Lazy<EmptyTypeLocator> _instance = new Lazy<EmptyTypeLocator>(() => new EmptyTypeLocator());

        private EmptyTypeLocator()
        {
            _types = new List<Type>().AsReadOnly();
        }

        public static EmptyTypeLocator Instance => _instance.Value;

        public IReadOnlyList<Type> GetTypes()
        {
            return _types;
        }
    }
}
