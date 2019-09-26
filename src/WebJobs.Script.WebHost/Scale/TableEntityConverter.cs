// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Scale
{
    /// <summary>
    /// Class providing methods to convert between DynamicTableEntity and custom pocos
    /// </summary>
    internal static class TableEntityConverter
    {
        public static DynamicTableEntity ToEntity(object o,
            string partitionKey = null,
            string rowKey = null,
            DateTimeOffset? timeStamp = null,
            string etag = null)
        {
            var entity = new DynamicTableEntity
            {
                RowKey = rowKey,
                PartitionKey = partitionKey,
                Properties = new Dictionary<string, EntityProperty>()
            };

            if (timeStamp.HasValue)
            {
                entity.Timestamp = timeStamp.Value;
            }

            if (!string.IsNullOrWhiteSpace(etag))
            {
                entity.ETag = etag;
            }

            var jo = JObject.FromObject(o);
            foreach (var prop in jo.Properties())
            {
                if (TryGetEntityProperty(prop, out EntityProperty entityProperty))
                {
                    entity.Properties.Add(prop.Name, entityProperty);
                }
            }

            return entity;
        }

        public static object ToObject(Type type, DynamicTableEntity entity)
        {
            return ToObject(type, entity.Properties);
        }

        public static TOutput ToObject<TOutput>(IDictionary<string, EntityProperty> properties)
        {
            return (TOutput)ToObject(typeof(TOutput), properties);
        }

        public static object ToObject(Type type, IDictionary<string, EntityProperty> properties)
        {
            var jo = new JObject();
            foreach (var pair in properties)
            {
                ApplyProperty(jo, pair.Key, pair.Value);
            }
            return jo.ToObject(type);
        }

        public static bool TryGetEntityProperty(JProperty property, out EntityProperty entityProperty)
        {
            entityProperty = null;
            var value = property.Value;

            switch (value.Type)
            {
                case JTokenType.Bytes:
                    entityProperty = new EntityProperty(value.ToObject<byte[]>());
                    return true;
                case JTokenType.Boolean:
                    entityProperty = new EntityProperty(value.ToObject<bool>());
                    return true;
                case JTokenType.Date:
                    entityProperty = new EntityProperty(value.ToObject<DateTime>());
                    return true;
                case JTokenType.Float:
                    entityProperty = new EntityProperty(value.ToObject<double>());
                    return true;
                case JTokenType.Guid:
                    entityProperty = new EntityProperty(value.ToObject<Guid>());
                    return true;
                case JTokenType.Integer:
                    entityProperty = new EntityProperty(value.ToObject<int>());
                    return true;
                case JTokenType.String:
                    entityProperty = new EntityProperty(value.ToObject<string>());
                    return true;
                default:
                    return false;
            }
        }

        public static void ApplyProperty(JObject jo, string name, EntityProperty entityProperty)
        {
            switch (entityProperty.PropertyType)
            {
                case EdmType.Binary:
                    jo.Add(name, new JValue(entityProperty.BinaryValue));
                    return;
                case EdmType.Boolean:
                    jo.Add(name, new JValue(entityProperty.BooleanValue));
                    return;
                case EdmType.DateTime:
                    jo.Add(name, new JValue(entityProperty.DateTime));
                    return;
                case EdmType.Double:
                    jo.Add(name, new JValue(entityProperty.DoubleValue));
                    return;
                case EdmType.Guid:
                    jo.Add(name, new JValue(entityProperty.GuidValue));
                    return;
                case EdmType.Int32:
                    jo.Add(name, new JValue(entityProperty.Int32Value));
                    return;
                case EdmType.Int64:
                    jo.Add(name, new JValue(entityProperty.Int64Value));
                    return;
                case EdmType.String:
                    jo.Add(name, new JValue(entityProperty.StringValue));
                    return;
                default:
                    return;
            }
        }
    }
}
