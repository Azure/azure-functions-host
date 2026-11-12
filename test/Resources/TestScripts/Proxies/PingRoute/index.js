module.exports = async function (context, req) {
    if ("return_incoming_url" in req.headers) {
        return { status: 200, body: req.url };
    }

    if ("return_empty_body" in req.headers) {
        return { status: 204 };
    }

    return { status: 200, body: "Pong" };
};
