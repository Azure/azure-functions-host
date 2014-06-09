using System;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class RandomNameResolver : INameResolver
    {
        // Convert to lowercase because many Azure services expect only lowercase
        private readonly string _randomString = Guid.NewGuid().ToString("N").ToLower();

        public string Resolve(string name)
        {
            if (name == "rnd")
            {
                return _randomString;
            }

            throw new NotSupportedException("Cannot resolve name: " + name);
        }
    }
}
