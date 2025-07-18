public static string Run(HttpRequest req)
{
    if (req.Headers.ContainsKey("return_test_header"))
    {
        req.HttpContext.Response.Headers.Add("test_header_from_function", "test_header_from_function_value");
    }
    if (req.Headers.ContainsKey("return_201"))
    {
        req.HttpContext.Response.StatusCode = 201;
    }
    if (req.Headers.ContainsKey("redirect"))
    {
        req.HttpContext.Response.Redirect("http://www.redirects-regardless.com/"); ;
    }
    return "Pong";
}
