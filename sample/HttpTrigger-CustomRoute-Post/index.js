module.exports = function (context, req) {
    context.bindings.product = req.body;

    var res = {
        status: 201,
        body: 'Product created'
    };

    context.done(null, res);
}
