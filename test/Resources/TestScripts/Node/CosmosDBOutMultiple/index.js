module.exports = function (context, input) {
    context.log('Node.js function triggered with input', input);

    context.bindings.items = JSON.stringify([
        {
            "id": input + "-0",
            "text": input
        },
        {
            "id": input + "-1",
            "text": input
        }]);

    context.done();
}