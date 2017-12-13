// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Routing;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISwaggerDocumentManager
    {
        /// <summary>
        /// Retrieves the Swagger document
        /// </summary>
        /// <returns>The Swagger document as a JSON Object, if <see cref="SwaggerDocumentMode"/> is set to manual, Otherwise null</returns>
        Task<JObject> GetSwaggerDocumentAsync();

        /// <summary>
        /// Deletes the Swagger document
        /// </summary>
        /// <returns>True if Swagger document was successfully deleted, Otherwise false</returns>
        Task<bool> DeleteSwaggerDocumentAsync();

        /// <summary>
        /// Adds or Updates SwaggerDocument
        /// </summary>
        /// <param name="swaggerDocumentJson">The JSON object for adding or updating SwaggerDocument</param>
        /// <returns>The JSON Object that was created or updated</returns>
        Task<JObject> AddOrUpdateSwaggerDocumentAsync(JObject swaggerDocumentJson);

        /// <summary>
        /// Generates a Swagger document as a JSON object using the information present in the route collection.
        /// </summary>
        /// <param name="routes">The mapped http routes</param>
        /// <returns>The Swagger document</returns>
        JObject GenerateSwaggerDocument(HttpRouteCollection routes);
    }
}