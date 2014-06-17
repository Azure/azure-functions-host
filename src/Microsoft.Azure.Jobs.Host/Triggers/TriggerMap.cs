using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    // In-memory 
    internal class TriggerMap : ITriggerMap
    {
        Dictionary<string, Trigger[]> _storage = new Dictionary<string, Trigger[]>();

        [JsonProperty]
        Dictionary<string, Trigger[]> Storage
        {
            get
            {
                return _storage;
            }
            set
            {
                _storage = value;
            }
        }
        public static JsonSerializerSettings NewSettings()
        {
            var settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            return settings;
        }

        public static JsonSerializerSettings _settings = NewSettings();

        public Trigger[] GetTriggers(string scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }

            Trigger[] ts;
            _storage.TryGetValue(scope, out ts);
            if (ts == null)
            {
                return new Trigger[0];
            }
            return ts;
        }

        public void AddTriggers(string scope, params Trigger[] triggers)
        {
            _storage[scope] = triggers;
        }

        public IEnumerable<string> GetScopes()
        {
            return _storage.Keys;
        }

        public override string ToString()
        {
            int count = 0;
            foreach (var kv in _storage)
            {
                count += kv.Value.Length;
            }
            return string.Format("{0} triggers", count);
        }
    }
}
