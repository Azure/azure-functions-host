module.exports = function (context, text) {
    if (context.bindings.entity.Text != text)
    {
        throw "Failed";
    }
    context.done();
}