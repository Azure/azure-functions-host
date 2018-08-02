﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NullScriptHostEnvironment : IScriptJobHostEnvironment
    {
        public void RestartHost()
        {
        }

        public void Shutdown()
        {
        }
    }
}
