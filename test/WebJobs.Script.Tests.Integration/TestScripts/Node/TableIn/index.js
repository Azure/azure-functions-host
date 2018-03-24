module.exports = function (context, input) {
    var result = {
        single: context.bindings.single,
        partition: context.bindings.partition,
        query: context.bindings.query
    };

    context.log('Result: ', JSON.stringify(result));

    context.done();
}