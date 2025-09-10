// Centralized helper for reading and sanitizing the x-azure-ref header for Extension Bundle downloads.
using System;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    internal static class ExtensionBundleHttpExtensions
    {
        internal const string AzureRefHeaderName = "x-azure-ref";
        private const int MaxAzureRefLength = 128;

        internal static string GetAzureRef(this HttpResponseMessage response)
        {
            try
            {
                if (response == null)
                {
                    return null;
                }

                if (response.Headers != null &&
                    response.Headers.TryGetValues(AzureRefHeaderName, out var values))
                {
                    return Sanitize(values.FirstOrDefault());
                }

                if (response.Content?.Headers != null &&
                    response.Content.Headers.TryGetValues(AzureRefHeaderName, out var contentValues))
                {
                    return Sanitize(contentValues.FirstOrDefault());
                }
            }
            catch
            {
                // Ignore header parsing issues.
            }

            return null;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (!char.IsControl(ch))
                {
                    sb.Append(ch);
                }
            }

            var cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0)
            {
                return null;
            }

            if (cleaned.Length > MaxAzureRefLength)
            {
                cleaned = cleaned.Substring(0, MaxAzureRefLength);
            }

            return cleaned;
        }
    }
}
