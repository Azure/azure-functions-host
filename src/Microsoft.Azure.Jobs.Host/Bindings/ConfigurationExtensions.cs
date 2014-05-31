using System;

namespace Microsoft.Azure.Jobs
{
    internal static class ConfigurationExtensions
    {
        public static ICloudTableBinder GetTableBinder(this IConfiguration config, Type targetType)
        {
            foreach (var provider in config.TableBinders)
            {
                var binder = provider.TryGetBinder(targetType);
                if (binder != null)
                {
                    return binder;
                }
            }
            return null;
        }
    }
}
