using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal static class RandomExtensions
    {
        public static double Next(this Random random, double minValue, double maxValue)
        {
            if (random == null)
            {
                throw new ArgumentNullException("random");
            }

            return (maxValue - minValue) * random.NextDouble() + minValue;
        }
    }
}
