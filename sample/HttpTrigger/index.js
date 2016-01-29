module.exports = function (context) {
    context.log('Node.js HTTP trigger function processed a request. Name=' + context.req.query.name);

    if (typeof context.req.query.name == 'undefined') {
        context.done(null, "Please pass a name on the query string");
    }
    else {
        context.done(null, "Hello " + context.req.query.name);
    }
}
