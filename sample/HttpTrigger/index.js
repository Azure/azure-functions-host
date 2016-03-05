module.exports = function (req, context) {
    context.log('Node.js HTTP trigger function processed a request. Name=' + req.query.name);

    var headerValue = req.headers['test-header'];
    if (headerValue) {
        context.log('test-header=' + headerValue);
    }

    if (typeof req.query.name == 'undefined') {
        context.done(null, "Please pass a name on the query string");
    }
    else {
        context.done(null, "Hello " + req.query.name);
    }
}
