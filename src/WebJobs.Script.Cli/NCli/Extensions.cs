using System;
using System.Collections;
using System.Collections.Generic;

namespace NCli
{
    internal static class Extensions
    {
        public static bool IsGenericEnumerable(this Type type)
        {
            return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static Type GetEnumerableType(this Type type)
        {
            var genericArguments = type.GetGenericArguments();
            if (type.IsGenericType && genericArguments.Length > 0)
            {
                return type.GetGenericArguments()[0];
            }
            else
            {
                return null;
            }
        }

        public static IList CreateList(this Type myType)
        {
            var genericListType = typeof(List<>).MakeGenericType(myType);
            return (IList)Activator.CreateInstance(genericListType);
        }
    }
}
