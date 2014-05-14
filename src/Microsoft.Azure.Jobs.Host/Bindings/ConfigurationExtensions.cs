using System;

namespace Microsoft.Azure.Jobs
{
    internal static class ConfigurationExtensions
    {
        public static ICloudTableBinder GetTableBinder(this IConfiguration config, Type targetType, bool isReadOnly)
        {
            foreach (var provider in config.TableBinders)
            {
                var binder = provider.TryGetBinder(targetType, isReadOnly);
                if (binder != null)
                {
                    return binder;
                }
            }
            return null;
        }

        public static ICloudBlobBinder GetBlobBinder(this IConfiguration config, Type targetType, bool isInput)
        {
            foreach (var provider in config.BlobBinders)
            {
                var binder = provider.TryGetBinder(targetType, isInput);
                if (binder != null)
                {
                    return binder;
                }
            }
            return null;
        }
    }
}
