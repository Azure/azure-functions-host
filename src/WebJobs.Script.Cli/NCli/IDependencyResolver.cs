using System;

namespace NCli
{
    public interface IDependencyResolver
    {
        T GetService<T>();
        object GetService(Type type);
        void RegisterService<T>(object obj);
    }
}
