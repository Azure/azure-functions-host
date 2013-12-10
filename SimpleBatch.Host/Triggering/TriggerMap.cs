using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace TriggerService
{
    public interface ITriggerMap
    {
        // Scope can be a user's site. 
        Trigger[] GetTriggers(string scope);

        void AddTriggers(string scope, params Trigger[] triggers);

        IEnumerable<string> GetScopes();

        void ClearTriggers(string scope);
    }

    public static class ITriggerMapExtensions
    {
        public static IEnumerable<Trigger> GetTriggers(this ITriggerMap x)
        {
            foreach (var scope in x.GetScopes())
            {
                foreach (var trigger in x.GetTriggers(scope))
                {
                    yield return trigger;
                }
            }
        }
    }

    // In-memory 
    public class TriggerMap : ITriggerMap
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

        public static string SaveJson(ITriggerMap map)
        {
            string content = JsonConvert.SerializeObject(map, _settings);
            return content;
        }

        public static TriggerMap LoadJson(string json)
        {
            var result = JsonConvert.DeserializeObject<TriggerMap>(json, _settings);
            return result;
        }


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

        public void ClearTriggers(string scope)
        {
            _storage.Remove(scope);
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
