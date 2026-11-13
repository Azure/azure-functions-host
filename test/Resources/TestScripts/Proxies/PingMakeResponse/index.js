module.exports = async function (context, req) {
    const response = { status: 200, body: "Pong", headers: {} };

    if ("return_test_header" in req.headers) {
        response.headers.test_header_from_function = "test_header_from_function_value";
    }
    if ("return_201" in req.headers) {
        response.status = 201;
    }
    if ("redirect" in req.headers) {
        response.status = 302;
        response.headers.location = "http://www.redirects-regardless.com/";
    }

    return response;
};
