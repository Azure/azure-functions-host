// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Dashboard.Data;
using Microsoft.Azure.WebJobs;

namespace Dashboard.ViewModels
{
    public class TriggerReasonViewModel
    {
        internal FunctionInstanceSnapshot UnderlyingObject { get; private set; }

        internal TriggerReasonViewModel(FunctionInstanceSnapshot underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public Guid ParentGuid
        {
            get { return UnderlyingObject.ParentId.HasValue ? UnderlyingObject.ParentId.Value : Guid.Empty; }
        }

        public Guid ChildGuid
        {
            get { return UnderlyingObject.Id; }
        }

        public override string ToString()
        {
            return UnderlyingObject.Reason;
        }
    }
}
