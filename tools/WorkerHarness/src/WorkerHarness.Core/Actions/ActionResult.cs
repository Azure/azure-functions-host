// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    public enum StatusCode
    {
        Success,
        Failure,
    }

    public class ActionResult
    {
        public StatusCode Status { get; set; }

        public string Message { get; set; } = string.Empty;

        public IList<string> ErrorMessages { get; set; } = new List<string>();

        public IList<string> VerboseErrorMessages { get; set; } = new List<string>();
    }

}
