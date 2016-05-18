module.exports = function (context, input) {
    var items = [ input ];
    var item = {
        partitionKey: input.partitionKey,
        rowKey: input.rowKey++,
        stringProp: 'Amy',
        intProp: 456,
        boolProp: true,
        guidProp: 'd7cb566c-a0b2-433e-8429-4ffcfcef1942',
        floatProp: 687.234
    };
    items.push(item);

    context.bindings.items = items;

    context.done();
}