using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.StorageClient;
namespace TriggerService
{
    internal static class DictExtensions
    {
        public static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
        {
            TValue value;
            if (!dict.TryGetValue(key, out value))
            {
                value = new TValue();
                dict[key] = value;
            }
            return value;
        }
    }

    internal static partial class Utility
    {
        [DebuggerNonUserCode]
        public static bool DoesBlobExist(CloudBlob blob)
        {
            try
            {
                blob.FetchAttributes(); // force network call to test whether it exists
                return true;
            }
            catch
            {
                return false;
            }

        }
        public static DateTime? GetBlobModifiedUtcTime(CloudBlob blob)
        {
            if (!DoesBlobExist(blob))
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModifiedUtc;
            return time;
        }
    }
}