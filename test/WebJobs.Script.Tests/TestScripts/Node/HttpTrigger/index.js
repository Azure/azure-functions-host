module.exports = function (req, context) {
    context.log('Node.js HttpTrigger function invoked.');

    var res = {
        type: typeof req.body,
        body: req.body
    };

    context.done(null, res);
}