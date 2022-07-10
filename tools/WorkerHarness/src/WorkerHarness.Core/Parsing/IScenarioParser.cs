// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Parsing
{
    public interface IScenarioParser
    {
        /// <summary>
        /// Create a scenario object from a scenario file
        /// </summary>
        /// <param name="scenarioFile"></param>
        /// <returns cref="Scenario"></returns>
        Scenario Parse(string scenarioFile);
    }
}
