module.exports = function (context, text) {
    context.bindings.entity = {
        Id: 5,
        Text: text
    };
    context.done();
}