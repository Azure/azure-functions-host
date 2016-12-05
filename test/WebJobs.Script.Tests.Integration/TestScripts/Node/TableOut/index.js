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

    // add another item
    // using upper case for PK/RK as a regression test for case sensitivity
    item = {
        PartitionKey: input.partitionKey,
        RowKey: input.rowKey++,
        stringProp: 'Ruby',
        intProp: 789,
        boolProp: false,
        guidProp: 'EC96DC6A-1E9A-4CC7-81F9-649CF8C2E25B',
        floatProp: 987.21
    };
    items.push(item);

    context.bindings.items = items;

    context.done();
}