module.exports = function (context) {
    context.log('Node.js HTTP trigger function processed a request');
    context.done(null, "Hello " + context.req.query.name);
}