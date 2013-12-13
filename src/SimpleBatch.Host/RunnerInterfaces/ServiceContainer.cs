using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RunnerInterfaces
{
    // $$$ Not really using this. 
    // Provide basic service resolution
    internal interface IServiceContainer
    {
        T GetService<T>();
    }

    // Trivial container for service resolution.
    internal class ServiceContainer : IServiceContainer
    {
        Dictionary<Type, object> _map = new Dictionary<Type, object>();

        // Return null if no service
        public T GetService<T>() 
        {
            object o;
            _map.TryGetValue(typeof(T), out o);
            return (T) o;
        }

        public void SetService<T>(T instance)
        {
            _map[typeof(T)] = instance;
        }
    }
}
