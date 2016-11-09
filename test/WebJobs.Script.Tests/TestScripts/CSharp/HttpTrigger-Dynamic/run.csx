using System;
using System.Net;
using System.Net.Http;

public static HttpResponseMessage Run(dynamic input)
{
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent($"Name: {input.name}, Location: {input.location}")
    };
}