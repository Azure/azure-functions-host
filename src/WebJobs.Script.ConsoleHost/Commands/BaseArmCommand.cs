// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WebJobs.Script.ConsoleHost.Arm;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public abstract class BaseArmCommand : Command
    {
        public ArmManager _armManager { get; private set; }

        public BaseArmCommand()
        {
            _armManager = new ArmManager();
        }
    }
}
