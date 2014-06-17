using System;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    public static class BlobPathParser
    {
        public static LocalBlobDescriptor Parse(string input)
        {
            LocalBlobDescriptor descriptor;

            if (!TryParse(input, out descriptor))
            {
                throw new FormatException("The blob path must be in the format container/blob.");
            }

            return descriptor;
        }

        public static bool TryParse(string input, out LocalBlobDescriptor parsed)
        {
            if (input == null)
            {
                parsed = null;
                return false;
            }

            int slashIndex = input.IndexOf('/');

            // There must be at least one character before the slash and one chacater after the slash.
            if (slashIndex <= 0 || slashIndex == input.Length - 1)
            {
                parsed = null;
                return false;
            }

            parsed = new LocalBlobDescriptor
            {
                ContainerName = input.Substring(0, slashIndex),
                BlobName = input.Substring(slashIndex + 1)
            };
            return true;
        }
    }
}
