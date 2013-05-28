using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        // !!! Find better way to serialize
        public Dictionary<string, Trigger[]> Storage
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
