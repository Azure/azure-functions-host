module.exports = function (context) {
    context.log('Node.js HttpTrigger function invoked.');

    var res = {
        type: typeof context.req.body,
        body: context.req.body
    };

    context.done(null, res);
}