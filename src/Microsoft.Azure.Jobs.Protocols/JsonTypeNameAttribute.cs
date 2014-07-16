// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>
    /// Provides a key to use in place of the .NET type name when deserializing polymophic objects using
    /// <see cref="PolymorphicJsonConverter"/>.
    /// </summary>
#if PUBLICPROTOCOL
    public class JsonTypeNameAttribute : Attribute
#else
    internal class JsonTypeNameAttribute : Attribute
#endif
    {
        private readonly string _typeName;

        /// <summary>Initializes a new instance of the <see cref="JsonTypeNameAttribute"/> class.</summary>
        /// <param name="typeName">The type name to use for serialization.</param>
        public JsonTypeNameAttribute(string typeName)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException("typeName");
            }

            _typeName = typeName;
        }

        /// <summary>Gets the type name to use for serialization.</summary>
        public string TypeName
        {
            get { return _typeName; }
        }
    }
}
