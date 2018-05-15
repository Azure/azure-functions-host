public static string Run(HttpRequestMessage req)
{
    if (req.Headers.Contains("return_incoming_url"))
    {
        return req.RequestUri.OriginalString;
    }
    else
    {
        return "Pong";
    }
}
