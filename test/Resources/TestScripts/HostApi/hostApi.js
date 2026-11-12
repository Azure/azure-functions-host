exports.greeting = async function (context, req) {
    const name = req.query.name;
    return {
        status: name ? 200 : 400,
        headers: { "content-type": "text/plain" },
        body: name ? `Hello ${name}` : "Please pass a name on the query string or in the request body"
    };
};

exports.route = async function (context, req) {
    const category = req.params.category || null;
    const id = req.params.id ? Number(req.params.id) : null;
    const extra = req.params.extra || "";
    context.log(`ProductInfo: Category=${category || ""} Id=${id === null ? "" : id}`);
    context.log(`Parameters: category=${category || ""} id=${id === null ? "" : id} extra=${extra}`);
    return { status: 200, body: { category, id } };
};

exports.poco = async function (context, req) {
    const data = req.body || req.query;
    context.bindings.outBlob = data.Value;
    context.bindings.res = { status: 200 };
};

exports.manual = async function (context, input) {
    return context.bindings.blobIn;
};

exports.scenarios = async function (context, req) {
    const body = typeof req.body === "string" ? JSON.parse(req.body) : req.body;
    if (body.scenario === "staticWebApp") {
        return { status: 200, body: req.query.code };
    }

    throw new Error(`Unknown scenario: ${body.scenario}`);
};

exports.functionAuth = async function (context, req) {
    return { status: 200, body: "Success" };
};
