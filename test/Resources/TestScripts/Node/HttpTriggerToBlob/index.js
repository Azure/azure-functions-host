module.exports = function (context, req) {
    console.log(context.bindingData);
    var result = req.body.value + req.body.id + context.bindingData.headers.value,
        response = {
            status: 200,
            body: result,
            headers: {
                'Content-Type': 'text/plain'
            }
        };

    context.bindings.outBlob = result;

    context.done(null, response);
};