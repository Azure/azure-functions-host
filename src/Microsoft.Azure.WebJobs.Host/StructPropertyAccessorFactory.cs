// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host
{
    internal class StructPropertyAccessorFactory<TReflected> : IPropertyAccessorFactory<TReflected>
        where TReflected : struct
    {
        private static readonly StructPropertyAccessorFactory<TReflected> _instance =
            new StructPropertyAccessorFactory<TReflected>();

        private StructPropertyAccessorFactory()
        {
        }

        public static StructPropertyAccessorFactory<TReflected> Instance
        {
            get { return _instance; }
        }

        public IPropertyGetter<TReflected, TProperty> CreateGetter<TProperty>(PropertyInfo property)
        {
            return StructPropertyGetter<TReflected, TProperty>.Create(property);
        }

        public IPropertySetter<TReflected, TProperty> CreateSetter<TProperty>(PropertyInfo property)
        {
            return StructPropertySetter<TReflected, TProperty>.Create(property);
        }
    }
}
