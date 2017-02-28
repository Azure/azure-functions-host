module.exports = function (context, input) {
    context.log('Node.js function triggered with input', input);

    context.bindings.item = {
        id: input
    };

    context.done();
}