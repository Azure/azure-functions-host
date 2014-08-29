// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class PocoTableEntity
    {
        public static TValue ToPocoEntity<TValue>(DynamicTableEntity tableEntity) where TValue : new()
        {
            IDictionary<string, string> data = Normalize(tableEntity);
            return ObjectBinderHelpers.ConvertDictToObject<TValue>(data);
        }

        public static ITableEntity ToTableEntity(object pocoEntity)
        {
            IDictionary<string, string> data = ObjectBinderHelpers.ConvertObjectToDict(pocoEntity);
            if (!data.ContainsKey("PartitionKey"))
            {
                throw new InvalidOperationException("Table entity types must implement the property PartitionKey.");
            }

            if (!data.ContainsKey("RowKey"))
            {
                throw new InvalidOperationException("Table entity types must implement the property RowKey.");
            }

            return ToTableEntity(data["PartitionKey"], data["RowKey"], data);
        }

        public static ITableEntity ToTableEntity(string partitionKey, string rowKey, object pocoEntity)
        {
            IDictionary<string, string> data = ObjectBinderHelpers.ConvertObjectToDict(pocoEntity);
            return ToTableEntity(partitionKey, rowKey, data);
        }

        private static DynamicTableEntity ToTableEntity(string partitionKey, string rowKey, IDictionary<string, string> values)
        {
            DynamicTableEntity entity = new DynamicTableEntity(partitionKey, rowKey);

            foreach (KeyValuePair<string, string> property in values)
            {
                string propertyName = property.Key;

                if (!IsSystemProperty(propertyName))
                {
                    entity.Properties.Add(propertyName, new EntityProperty(property.Value));
                }
                else if (propertyName.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    entity.Timestamp = DateTimeOffset.Parse(property.Value);
                }
                else if (propertyName.Equals("ETag", StringComparison.OrdinalIgnoreCase))
                {
                    entity.ETag = property.Value;
                }
            }

            return entity;
        }

        private static IDictionary<string, string> Normalize(DynamicTableEntity item)
        {
            IDictionary<string, string> properties = new Dictionary<string, string>();

            properties["PartitionKey"] = item.PartitionKey;
            properties["RowKey"] = item.RowKey;
            properties["Timestamp"] = item.Timestamp.ToString("o", CultureInfo.InvariantCulture);
            properties["ETag"] = item.ETag;

            foreach (KeyValuePair<string, EntityProperty> property in item.Properties)
            {
                if (!properties.ContainsKey(property.Key))
                {
                    properties.Add(property.Key, Normalize(property.Value));
                }
            }

            return properties;
        }

        private static string Normalize(EntityProperty property)
        {
            switch (property.PropertyType)
            {
                case EdmType.String:
                    return property.StringValue;
                case EdmType.Binary:
                    return property.BinaryValue != null ? Convert.ToBase64String(property.BinaryValue) : null;
                case EdmType.Boolean:
                    return property.BooleanValue.HasValue ? property.BooleanValue.Value.ToString().ToLowerInvariant() : null;
                case EdmType.DateTime:
                    return property.DateTimeOffsetValue.HasValue ? property.DateTimeOffsetValue.Value.UtcDateTime.ToString("O") : null;
                case EdmType.Double:
                    return property.DoubleValue.HasValue ? property.DoubleValue.ToString() : null;
                case EdmType.Guid:
                    return property.GuidValue.HasValue ? property.GuidValue.ToString() : null;
                case EdmType.Int32:
                    return property.Int32Value.HasValue ? property.Int32Value.ToString() : null;
                case EdmType.Int64:
                    return property.Int64Value.HasValue ? property.Int64Value.ToString() : null;
                default:
                    throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Unsupported EDM property type {0}", property.PropertyType));
            }
        }

        private static bool IsSystemProperty(string name)
        {
            if (String.Equals("PartitionKey", name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else if (String.Equals("RowKey", name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else if (String.Equals("Timestamp", name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else if (String.Equals("ETag", name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
