using System;
using System.Collections.Generic;
using System.Reflection;

namespace WebJobs.Script.ConsoleHost.Arm.Extensions
{
    public static class Extensions
    {
        public static string NullStatus(this object o)
        {
            return o == null ? "Null" : "NotNull";
        }

        public static T MergeWith<T, U, M>(this T target, U source, Func<U, M> selector = null)
            where T : class
            where U : class
            where M : class
        {
            object _source = null;
            if (source != null && selector != null)
            {
                _source = selector(source);
            }
            
            if (_source == null)
                return target;

            foreach (var sourceProperty in _source.GetType().GetProperties())
            {
                var targetProperty = target.GetType().GetProperty(sourceProperty.Name);
                var targetPropertyEnum = target.GetType().GetProperty(sourceProperty.Name + "Enum");
                new List<PropertyInfo>() { targetProperty, targetPropertyEnum }.ForEach(property =>
                {
                    if (property == null) return;
                    var st = sourceProperty.PropertyType;
                    var tt = property.PropertyType;

                    Func<bool> validReadableProperties = () => (sourceProperty.CanRead && property.CanRead);
                    Func<bool> typesMatch = () => st == tt;
                    Func<bool> enumMatch = () => tt.IsEnum && Enum.GetUnderlyingType(tt) == st;
                    Func<bool> nullableEnumMatch = () => Nullable.GetUnderlyingType(tt) != null && Nullable.GetUnderlyingType(tt).IsEnum && Enum.GetUnderlyingType(Nullable.GetUnderlyingType(tt)) == st;
                    Func<bool> nullableMatch = () => Nullable.GetUnderlyingType(tt) == st;


                    if (validReadableProperties() && (typesMatch() || nullableMatch() || enumMatch()))
                    {
                        property.SetValue(target, sourceProperty.GetValue(_source));
                    }
                    else if (validReadableProperties() && nullableEnumMatch())
                    {
                        property.SetValue(target, Enum.ToObject(Nullable.GetUnderlyingType(tt), sourceProperty.GetValue(_source)));
                    }
                });
            }
            return target;
        }
    }
}