// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface ISecretsManager
    {
        IDictionary<string, string> GetSecrets();
        void SetSecret(string name, string value);
    }
}
