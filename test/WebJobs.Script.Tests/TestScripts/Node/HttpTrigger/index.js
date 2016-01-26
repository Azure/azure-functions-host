module.exports = function (context) {
    var res = {
        type: typeof context.req.body,
        body: context.req.body
    };

    context.done(null, res);
}