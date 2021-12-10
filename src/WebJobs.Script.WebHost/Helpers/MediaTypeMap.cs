// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.StaticFiles;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class MediaTypeMap
    {
        private static readonly MediaTypeMap _defaultInstance = new MediaTypeMap();
        private static readonly FileExtensionContentTypeProvider _mimeMapping = new FileExtensionContentTypeProvider();
        private readonly ConcurrentDictionary<string, MediaTypeHeaderValue> _mediatypeMap = CreateMediaTypeMap();
        private static readonly MediaTypeHeaderValue _defaultMediaType = MediaTypeHeaderValue.Parse("application/octet-stream");

        public static MediaTypeMap Default
        {
            get { return _defaultInstance; }
        }

        public MediaTypeHeaderValue GetMediaType(string fileExtension)
        {
            ArgumentNullException.ThrowIfNull(fileExtension);

            return _mediatypeMap.GetOrAdd(fileExtension,
                static (extension) =>
                {
                    try
                    {
                        if (_mimeMapping.TryGetContentType(extension, out string mediaTypeValue))
                        {
                            if (MediaTypeHeaderValue.TryParse(mediaTypeValue, out MediaTypeHeaderValue mediaType))
                            {
                                return mediaType;
                            }
                        }
                        return _defaultMediaType;
                    }
                    catch
                    {
                        return _defaultMediaType;
                    }
                });
        }

        private static ConcurrentDictionary<string, MediaTypeHeaderValue> CreateMediaTypeMap()
        {
            var dictionary = new ConcurrentDictionary<string, MediaTypeHeaderValue>(StringComparer.OrdinalIgnoreCase);
            dictionary.TryAdd(".js", MediaTypeHeaderValue.Parse("application/javascript"));
            dictionary.TryAdd(".json", MediaTypeHeaderValue.Parse("application/json"));

            // Add media type for markdown
            dictionary.TryAdd(".md", MediaTypeHeaderValue.Parse("text/plain"));

            return dictionary;
        }
    }
}