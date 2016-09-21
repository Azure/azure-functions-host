module.exports = function (context, req) {
    context.log('Node.js HTTP trigger function processed a request. Name=%s', req.query.name);

    var headerValue = req.headers['test-header'];
    if (headerValue) {
        context.log('test-header=' + headerValue);
    }

    var res;
    if (typeof req.query.name == 'undefined') {
        res = {
            status: 400,
            body: "Please pass a name on the query string",
            headers: {
                'Content-Type': 'text/plain'
            }
        };
    }
    else {
        res = {
            status: 200,
            body: "Hello " + req.query.name,
            headers: {
                'Content-Type': 'text/plain'
            }
        };
    }

    context.done(null, res);
}
