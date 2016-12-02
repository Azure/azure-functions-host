module.exports = function (context, input) {
    context.bindings.entity = {
        Id: 5,
        Text: input.value
    };
    context.done();
}