﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NullScriptHostEnvironment : IScriptHostEnvironment
    {
        public void RestartHost()
        {
        }

        public void Shutdown()
        {
        }
    }
}
