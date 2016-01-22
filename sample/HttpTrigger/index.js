module.exports = function (context) {
    context.log('Node.js HTTP trigger function processed request ' + context.req.body);
    context.done(null, context.req.body);
}