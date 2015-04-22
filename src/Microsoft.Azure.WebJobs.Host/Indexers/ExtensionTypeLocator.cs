// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class ExtensionTypeLocator : IExtensionTypeLocator
    {
        private readonly ITypeLocator _typeLocator;

        public ExtensionTypeLocator(ITypeLocator typeLocator)
        {
            if (typeLocator == null)
            {
                throw new ArgumentNullException("typeLocator");
            }

            _typeLocator = typeLocator;
        }

        private IReadOnlyList<Type> _cloudBlobStreamBinderTypes;

        public IReadOnlyList<Type> GetCloudBlobStreamBinderTypes()
        {
            if (_cloudBlobStreamBinderTypes == null)
            {
                _cloudBlobStreamBinderTypes = GetCloudBlobStreamBinderTypes(_typeLocator.GetTypes());
            }

            return _cloudBlobStreamBinderTypes;
        }

        // Search for any types that implement ICloudBlobStreamBinder<T>
        internal static IReadOnlyList<Type> GetCloudBlobStreamBinderTypes(IEnumerable<Type> types)
        {
            List<Type> cloudBlobStreamBinderTypes = new List<Type>();

            foreach (Type type in types)
            {
                try
                {
                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        if (interfaceType.IsGenericType)
                        {
                            Type interfaceGenericDefinition = interfaceType.GetGenericTypeDefinition();

                            if (interfaceGenericDefinition == typeof(ICloudBlobStreamBinder<>))
                            {
                                cloudBlobStreamBinderTypes.Add(type);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return cloudBlobStreamBinderTypes;
        }
    }
}
