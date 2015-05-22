// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard
{
    public class HostVersionModel
    {
        public HostVersionModel(string label, string link)
        {
            Link = link;
            Label = label;
        }

        public string Label { get; private set; }
        public string Link { get; private set; }
    }
}