// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Xunit;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class TableTests
    {
        private const string TriggerQueueName = "input";
        private const string TableName = "Table";
        private const string PartitionKey = "PK";
        private const string RowKey = "RK";
        private const string PropertyName = "Property";

        [Fact]
        public void Table_IfBoundToCustomTableBindingExtension_BindsCorrectly()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // register our custom table binding extension provider
            DefaultExtensionRegistry extensions = new DefaultExtensionRegistry();
            extensions.RegisterExtension<IArgumentBindingProvider<ITableArgumentBinding>>(new CustomTableArgumentBindingProvider());

            // Act
            RunTrigger(account, typeof(CustomTableBindingExtensionProgram), extensions);

            // Assert
            Assert.Equal(TableName, CustomTableBinding<Poco>.Table.Name);
            Assert.True(CustomTableBinding<Poco>.AddInvoked);
            Assert.True(CustomTableBinding<Poco>.DeleteInvoked);
        }

        [Fact]
        public void Table_IfBoundToCloudTable_BindsAndCreatesTable()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            CloudTable result = RunTrigger<CloudTable>(account, typeof(BindToCloudTableProgram),
                (s) => BindToCloudTableProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(TableName, result.Name);
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
        }

        [Fact]
        public void Table_IfBoundToICollectorJObject_AddInsertsEntity()    
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            // Act
            RunTrigger(account, typeof(BindToICollectorJObjectProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            Assert.NotNull(entity.Properties);

            AssertPropertyValue(entity, "ValueStr", "abcdef");
            AssertPropertyValue(entity, "ValueNum", 123);         
        }

        [Fact]
        public void Table_IfBoundToICollectorITableEntity_AddInsertsEntity()
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            // Act
            RunTrigger(account, typeof(BindToICollectorITableEntityProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.String, property.PropertyType);
            Assert.Equal(expectedValue, property.StringValue);
        }

        [Fact]
        public void Table_IfBoundToICollectorPoco_AddInsertsEntity()
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            // Act
            RunTrigger(account, typeof(BindToICollectorPocoProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.String, property.PropertyType);
            Assert.Equal(expectedValue, property.StringValue);
        }

        [Fact]
        public void Table_IfBoundToICollectorPoco_AddInsertsUsingNativeTableTypes()
        {
            // Arrange
            PocoWithAllTypes expected = new PocoWithAllTypes
            {
                PartitionKey = PartitionKey,
                RowKey = RowKey,
                BooleanProperty = true,
                NullableBooleanProperty = null,
                ByteArrayProperty = new byte[] { 0x12, 0x34 },
                DateTimeProperty = DateTime.Now,
                NullableDateTimeProperty = null,
                DateTimeOffsetProperty = DateTimeOffset.MaxValue,
                NullableDateTimeOffsetProperty = null,
                DoubleProperty = 3.14,
                NullableDoubleProperty = null,
                GuidProperty = Guid.NewGuid(),
                NullableGuidProperty = null,
                Int32Property = 123,
                NullableInt32Property = null,
                Int64Property = 456,
                NullableInt64Property = null,
                StringProperty = "abc",
                PocoProperty = new Poco
                {
                    PartitionKey = "def",
                    RowKey = "ghi",
                    Property = "jkl"
                }
            };
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(JsonConvert.SerializeObject(expected)));

            // Act
            RunTrigger(account, typeof(BindToICollectorPocoWithAllTypesProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.Equal(expected.PartitionKey, entity.PartitionKey);
            Assert.Equal(expected.RowKey, entity.RowKey);
            IDictionary<string, EntityProperty> properties = entity.Properties;
            AssertNullablePropertyEqual(expected.BooleanProperty, EdmType.Boolean, properties, "BooleanProperty",
                (p) => p.BooleanValue);
            AssertPropertyNull(EdmType.Boolean, properties, "NullableBooleanProperty", (p) => p.BooleanValue);
            AssertPropertyEqual(expected.ByteArrayProperty, EdmType.Binary, properties, "ByteArrayProperty",
                (p) => p.BinaryValue);
            AssertNullablePropertyEqual(expected.DateTimeProperty, EdmType.DateTime, properties, "DateTimeProperty",
                (p) => p.DateTime);
            AssertPropertyNull(EdmType.DateTime, properties, "NullableDateTimeProperty", (p) => p.DateTime);
            AssertNullablePropertyEqual(expected.DateTimeOffsetProperty, EdmType.DateTime, properties,
                "DateTimeOffsetProperty", (p) => p.DateTime);
            AssertPropertyNull(EdmType.DateTime, properties, "NullableDateTimeOffsetProperty",
                (p) => p.DateTimeOffsetValue);
            AssertNullablePropertyEqual(expected.DoubleProperty, EdmType.Double, properties, "DoubleProperty",
                (p) => p.DoubleValue);
            AssertPropertyNull(EdmType.Double, properties, "NullableDoubleProperty", (p) => p.DoubleValue);
            AssertNullablePropertyEqual(expected.GuidProperty, EdmType.Guid, properties, "GuidProperty",
                (p) => p.GuidValue);
            AssertPropertyNull(EdmType.Guid, properties, "NullableGuidProperty", (p) => p.GuidValue);
            AssertNullablePropertyEqual(expected.Int32Property, EdmType.Int32, properties, "Int32Property",
                (p) => p.Int32Value);
            AssertPropertyNull(EdmType.Int32, properties, "NullableInt32Property", (p) => p.Int32Value);
            AssertNullablePropertyEqual(expected.Int64Property, EdmType.Int64, properties, "Int64Property",
                (p) => p.Int64Value);
            AssertPropertyNull(EdmType.Int64, properties, "NullableInt64Property", (p) => p.Int64Value);
            AssertPropertyEqual(expected.StringProperty, EdmType.String, properties, "StringProperty",
                (p) => p.StringValue);
            AssertPropertyEqual(JsonConvert.SerializeObject(expected.PocoProperty, Formatting.Indented), EdmType.String,
                properties, "PocoProperty", (p) => p.StringValue);
        }

        [Fact]
        public void Table_IfBoundToIQueryableDynamicTableEntityAndDoesNotExist_BindsAndDoesNotCreateTable()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            IQueryable<DynamicTableEntity> result = RunTrigger<IQueryable<DynamicTableEntity>>(account,
                typeof(BindToIQueryableDynamicTableEntityProgram),
                (s) => BindToIQueryableDynamicTableEntityProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.False(table.Exists());
        }

        [Fact]
        public void Table_IfBoundToIQueryableDynamicTableEntityAndExists_Binds()
        {
            // Arrange
            Guid expectedValue = Guid.NewGuid();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            table.CreateIfNotExists();
            Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
            {
                { PropertyName, new EntityProperty(expectedValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));

            // Act
            IQueryable<DynamicTableEntity> result = RunTrigger<IQueryable<DynamicTableEntity>>(account,
                typeof(BindToIQueryableDynamicTableEntityProgram),
                (s) => BindToIQueryableDynamicTableEntityProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            DynamicTableEntity[] entities = result.ToArray();
            Assert.Equal(1, entities.Length);
            DynamicTableEntity entity = entities[0];
            Assert.NotNull(entity);
            Assert.Equal(PartitionKey, entity.PartitionKey);
            Assert.Equal(RowKey, entity.RowKey);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.Guid, property.PropertyType);
            Assert.Equal(expectedValue, property.GuidValue);
        }

        private static void AssertNullablePropertyEqual<T>(T expected,
            EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, Nullable<T>> actualAccessor)
            where T : struct
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            Nullable<T> actualValue = actualAccessor.Invoke(property);
            Assert.True(actualValue.HasValue);
            Assert.Equal(expected, actualValue.Value);
        }

        private static void AssertPropertyValue(DynamicTableEntity entity, string propertyName, object expectedValue)
        {
            Assert.True(entity.Properties.ContainsKey(propertyName));
            EntityProperty property = entity.Properties[propertyName];
            Assert.NotNull(property);

            if (expectedValue is string)
            {
                Assert.Equal(EdmType.String, property.PropertyType);
                Assert.Equal(expectedValue, property.StringValue);
            }
            else if (expectedValue is int)
            {
                Assert.Equal(EdmType.Int32, property.PropertyType);
                Assert.Equal(expectedValue, property.Int32Value);
            }
            else {
                Assert.False(true, "test bug: unsupported property type: " + expectedValue.GetType().FullName);
            }
        }

        private static void AssertPropertyEqual<T>(T expected,
            EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, T> actualAccessor)
            where T : class
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            T actualValue = actualAccessor.Invoke(property);
            Assert.Equal(expected, actualValue);
        }

        private static void AssertPropertyNull<T>(EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, Nullable<T>> actualAccessor)
            where T : struct
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            Nullable<T> actualValue = actualAccessor.Invoke(property);
            Assert.False(actualValue.HasValue);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageAccount account, string queueName)
        {
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static void RunTrigger(IStorageAccount account, Type programType, IExtensionRegistry extensions = null)
        {
            FunctionalTest.RunTrigger(account, programType, extensions);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BindToCloudTableProgram
        {
            public static TaskCompletionSource<CloudTable> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] CloudTable table)
            {
                TaskSource.TrySetResult(table);
            }
        }

        private class BindToICollectorJObjectProgram 
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<JObject> table)
            {
                table.Add(JObject.FromObject( new {
                    PartitionKey = PartitionKey,
                    RowKey = RowKey,
                    ValueStr = "abcdef",
                    ValueNum = 123 
                }));
            }
        }

        private class BindToICollectorITableEntityProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<ITableEntity> table)
            {
                Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
                {
                    { PropertyName, new EntityProperty(message.AsString) }
                };
                table.Add(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));
            }
        }

        private class BindToICollectorPocoProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<Poco> table)
            {
                table.Add(new Poco { PartitionKey = PartitionKey, RowKey = RowKey, Property = message.AsString });
            }
        }

        private class BindToICollectorPocoWithAllTypesProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<PocoWithAllTypes> table)
            {
                PocoWithAllTypes entity = JsonConvert.DeserializeObject<PocoWithAllTypes>(message.AsString);
                table.Add(entity);
            }
        }

        private class BindToIQueryableDynamicTableEntityProgram
        {
            public static TaskCompletionSource<IQueryable<DynamicTableEntity>> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] IQueryable<DynamicTableEntity> table)
            {
                TaskSource.TrySetResult(table);
            }
        }

        private class CustomTableBindingExtensionProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] CustomTableBinding<Poco> table)
            {
                Poco entity = new Poco();
                table.Add(entity);
                table.Delete(entity);
            }
        }

        private class Poco
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public string Property { get; set; }
        }

        private class PocoWithAllTypes
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public bool BooleanProperty { get; set; }

            public bool? NullableBooleanProperty { get; set; }

            public byte[] ByteArrayProperty { get; set; }

            public DateTime DateTimeProperty { get; set; }

            public DateTime? NullableDateTimeProperty { get; set; }

            public DateTimeOffset DateTimeOffsetProperty { get; set; }

            public DateTimeOffset? NullableDateTimeOffsetProperty { get; set; }

            public double DoubleProperty { get; set; }

            public double? NullableDoubleProperty { get; set; }

            public Guid GuidProperty { get; set; }

            public Guid? NullableGuidProperty { get; set; }

            public int Int32Property { get; set; }

            public int? NullableInt32Property { get; set; }

            public long Int64Property { get; set; }

            public long? NullableInt64Property { get; set; }

            public string StringProperty { get; set; }

            public Poco PocoProperty { get; set; }
        }

        /// <summary>
        /// Binding type demonstrating how custom binding extensions can be used to bind to
        /// arbitrary types
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        public class CustomTableBinding<TEntity>
        {
            public static bool AddInvoked;
            public static bool DeleteInvoked;
            public static CloudTable Table;

            public CustomTableBinding(CloudTable table)
            {
                // this custom binding has the table, so can perform whatever storage
                // operations it needs to
                Table = table;
            }

            public void Add(TEntity entity)
            {
                // storage operations here
                AddInvoked = true;
            }

            public void Delete(TEntity entity)
            {
                // storage operations here
                DeleteInvoked = true;
            }

            internal Task FlushAsync(CancellationToken cancellationToken)
            {
                // complete and flush all storage operations
                return Task.FromResult(true);
            }
        }

        /// <summary>
        /// Demonstrates an example binding extension provider for Tables
        /// </summary>
        private class CustomTableArgumentBindingProvider : IArgumentBindingProvider<ITableArgumentBinding>
        {
            public ITableArgumentBinding TryCreate(ParameterInfo parameter)
            {
                // Determine whether the target is a Table paramter that we should bind to
                TableAttribute tableAttribute = parameter.GetCustomAttribute<TableAttribute>(inherit: false);
                if (tableAttribute == null ||
                    !parameter.ParameterType.IsGenericType ||
                    (parameter.ParameterType.GetGenericTypeDefinition() != typeof(CustomTableBinding<>)))
                {
                    return null;
                }

                // create the binding
                Type elementType = GetItemType(parameter.ParameterType);
                Type bindingType = typeof(CustomTableBindingExtension<>).MakeGenericType(elementType);

                return (ITableArgumentBinding)Activator.CreateInstance(bindingType);
            }

            private static Type GetItemType(Type queryableType)
            {
                Type[] genericArguments = queryableType.GetGenericArguments();
                var itemType = genericArguments[0];
                return itemType;
            }

            /// <summary>
            /// Custom Table binding extension, responsible for binding a <see cref="CloudTable"/> to a
            /// <see cref="CustomTableBinding<TElement>"/>
            /// </summary>
            /// <typeparam name="TElement"></typeparam>
            private class CustomTableBindingExtension<TElement> : ITableArgumentBinding
            {
                public FileAccess Access
                {
                    get { return FileAccess.ReadWrite; }
                }

                public Type ValueType { get { return typeof(CustomTableBinding<TElement>); } }

                public Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
                {
                    return Task.FromResult<IValueProvider>(new CustomTableValueBinder(value, ValueType));
                }

                private class CustomTableValueBinder : IValueBinder
                {
                    private readonly CloudTable _table;
                    private readonly Type _valueType;

                    public CustomTableValueBinder(CloudTable table, Type valueType)
                    {
                        _table = table;
                        _valueType = valueType;
                    }

                    public Type Type
                    {
                        get { return typeof(CustomTableBinding<TElement>); }
                    }

                    public object GetValue()
                    {
                        return new CustomTableBinding<TElement>(_table);
                    }

                    public Task SetValueAsync(object value, CancellationToken cancellationToken)
                    {
                        // this is where any queued up storage operations can be flushed
                        CustomTableBinding<TElement> tableBinding = value as CustomTableBinding<TElement>;
                        return tableBinding.FlushAsync(cancellationToken);
                    }

                    public string ToInvokeString()
                    {
                        return _table.Name;
                    }
                }
            }
        }
    }
}
