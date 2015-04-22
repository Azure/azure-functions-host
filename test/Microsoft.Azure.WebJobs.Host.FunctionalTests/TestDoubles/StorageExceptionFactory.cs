// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Xml;
using System.Xml.Linq;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal static class StorageExceptionFactory
    {
        public static StorageException Create(int httpStatusCode)
        {
            return Create(httpStatusCode, new XElement("Error", String.Empty));
        }

        public static StorageException Create(int httpStatusCode, string errorCode)
        {
            return Create(httpStatusCode, new XElement("Error", new XElement("Code", errorCode)));
        }

        private static StorageException Create(int httpStatusCode, XElement extendedErrorElement)
        {
            // Unfortunately, the RequestResult properties are all internal-only settable. ReadXml is the only way to
            // create a populated RequestResult instance.
            XElement requestResultElement = new XElement("RequestResult",
                new XElement("HTTPStatusCode", httpStatusCode),
                new XElement("HttpStatusMessage"),
                new XElement("TargetLocation"),
                new XElement("ServiceRequestID"),
                new XElement("ContentMd5"),
                new XElement("Etag"),
                new XElement("RequestDate"),
                new XElement("StartTime", DateTime.Now),
                new XElement("EndTime", DateTime.Now),
                extendedErrorElement);

            RequestResult result = new RequestResult();

            using (XmlReader reader = requestResultElement.CreateReader())
            {
                result.ReadXml(reader);
            }

            return new StorageException(result, null, null);
        }
    }
}
