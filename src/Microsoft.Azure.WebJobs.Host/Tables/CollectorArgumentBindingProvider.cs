// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CollectorArgumentBindingProvider : ITableArgumentBindingProvider
    {
        public ITableArgumentBinding TryCreate(Type parameterType)
        {
            if (!parameterType.IsGenericType ||
                (parameterType.GetGenericTypeDefinition() != typeof(ICollector<>)))
            {
                return null;
            }

            Type entityType = GetCollectorItemType(parameterType);

            if (!TableClient.ImplementsOrEqualsITableEntity(entityType))
            {
                TableClient.VerifyContainsProperty(entityType, "RowKey");
                TableClient.VerifyContainsProperty(entityType, "PartitionKey");
            }

            return CreateBinding(entityType);
        }

        private static Type GetCollectorItemType(Type queryableType)
        {
            Type[] genericArguments = queryableType.GetGenericArguments();
            var itemType = genericArguments[0];
            return itemType;
        }

        private static ITableArgumentBinding CreateBinding(Type entityType)
        {
            if (TableClient.ImplementsOrEqualsITableEntity(entityType))
            {
                Type genericType = typeof(TableEntityCollectorArgumentBinding<>).MakeGenericType(entityType);
                return (ITableArgumentBinding)Activator.CreateInstance(genericType);
            }
            else
            {
                Type genericType = typeof(PocoEntityCollectorArgumentBinding<>).MakeGenericType(entityType);
                return (ITableArgumentBinding)Activator.CreateInstance(genericType);
            }
        }

        private class TableEntityCollectorArgumentBinding<TElement> : ITableArgumentBinding
            where TElement : ITableEntity
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(ICollector<TElement>); }
            }

            public  Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
            {
                TableEntityWriter<TElement> tableWriter = new TableEntityWriter<TElement>(value);
                IValueProvider provider = new TableEntityCollectorBinder<TElement>(value, tableWriter, typeof(ICollector<TElement>));
                return Task.FromResult(provider);
            }
        }

        private class PocoEntityCollectorArgumentBinding<TElement> : ITableArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(ICollector<TElement>); }
            }

            public Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
            {
                PocoEntityWriter<TElement> collector = new PocoEntityWriter<TElement>(value);
                IValueProvider provider = new PocoEntityCollectorBinder<TElement>(value, collector, typeof(ICollector<TElement>));
                return Task.FromResult(provider);
            }
        }
    }
}
