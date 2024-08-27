// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Scale
{
    /// <summary>
    /// Class providing methods to convert between <see cref="TableEntity"/> and custom pocos.
    /// </summary>
    internal static class TableEntityConverter
    {
        public static TableEntity ToEntity(object o,
            string partitionKey = null,
            string rowKey = null,
            DateTimeOffset? timeStamp = null,
            string etag = null)
        {
            var entity = new TableEntity
            {
                RowKey = rowKey,
                PartitionKey = partitionKey
            };

            if (timeStamp.HasValue)
            {
                entity.Timestamp = timeStamp.Value;
            }

            if (!string.IsNullOrWhiteSpace(etag))
            {
                entity.ETag = new ETag(etag);
            }

            var jo = JObject.FromObject(o);
            foreach (var prop in jo.Properties())
            {
                if (TryGetEntityProperty(prop, out object value))
                {
                    entity.Add(prop.Name, value);
                }
            }

            return entity;
        }

        public static TOutput ToObject<TOutput>(IDictionary<string, object> properties)
        {
            return (TOutput)ToObject(typeof(TOutput), properties);
        }

        public static object ToObject(Type type, IDictionary<string, object> properties)
        {
            var jo = new JObject();
            foreach (var pair in properties)
            {
                jo.Add(pair.Key, new JValue(pair.Value));
            }
            return jo.ToObject(type);
        }

        public static bool TryGetEntityProperty(JProperty property, out object entityProperty)
        {
            entityProperty = null;
            var value = property.Value;

            switch (value.Type)
            {
                case JTokenType.Bytes:
                    entityProperty = value.ToObject<byte[]>();
                    return true;
                case JTokenType.Boolean:
                    entityProperty = value.ToObject<bool>();
                    return true;
                case JTokenType.Date:
                    entityProperty = value.ToObject<DateTime>();
                    return true;
                case JTokenType.Float:
                    entityProperty = value.ToObject<double>();
                    return true;
                case JTokenType.Guid:
                    entityProperty = value.ToObject<Guid>();
                    return true;
                case JTokenType.Integer:
                    // to handle both ints and longs, we normalize integer values
                    // to type long
                    entityProperty = value.ToObject<long>();
                    return true;
                case JTokenType.String:
                case JTokenType.TimeSpan:
                    entityProperty = value.ToObject<string>();
                    return true;
                default:
                    return false;
            }
        }
    }
}