// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public sealed class StructuredLogEntry
    {
        private readonly Dictionary<string, object> _properties = new Dictionary<string, object>();

        public StructuredLogEntry(string name)
            : this(Guid.NewGuid(), name)
        {
        }

        public StructuredLogEntry(Guid id, string name)
        {
            Id = id;
            Name = name;
            _properties = new Dictionary<string, object>();
        }

        /// <summary>
        /// Gets the event ID. This uniquely identifies this <see cref="StructuredLogEntry"/> instance.
        /// </summary
        public Guid Id { get; }

        /// <summary>
        /// Gets the event name. This identifies the type of event represented by the <see cref="StructuredLogEntry"/> instance.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Adds a log entry property.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The property value.</param>
        public void AddProperty(string name, object value)
        {
            if (string.Equals(nameof(Name), name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nameof(Id), name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"{name} is an invalid property name.", nameof(name));
            }

            _properties.Add(name, value);
        }

        /// <summary>
        /// Returns a JSON string representation of this object in a single line.
        /// </summary>
        /// <returns>A JSON string representation of this object in a single line.</returns>
        public string ToJsonLineString()
        {
            var resultObject = new JObject
            {
                ["name"] = Name,
                ["id"] = Id
            };
            foreach (var item in _properties)
            {
                resultObject.Add(item.Key, JToken.FromObject(item.Value));
            }

            return resultObject.ToString(Formatting.None);
        }
    }
}