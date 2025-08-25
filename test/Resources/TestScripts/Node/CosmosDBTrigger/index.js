module.exports = function (context, input) {
    context.log('Document Id: ', input[0].id);

    context.bindings.blob = input[0].id;

    context.done();
}