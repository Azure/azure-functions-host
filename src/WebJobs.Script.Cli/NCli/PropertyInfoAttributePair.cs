using System;
using System.Reflection;

namespace NCli
{
    internal class PropertyInfoPair<T> where T: Attribute
    {
        public PropertyInfo PropertyInfo { get; set; }
        public T Attribute { get; set; }
    }
}
