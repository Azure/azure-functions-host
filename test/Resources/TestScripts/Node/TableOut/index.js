module.exports = function (context, input) {
    var pk = input.PartitionKey || input.partitionKey;
    var rk = Number(input.RowKey || input.rowKey);

    var items = [
        {
            PartitionKey: pk,
            RowKey: String(rk),
            stringProp: input.stringProp,
            intProp: input.intProp,
            boolProp: input.boolProp,
            guidProp: input.guidProp,
            floatProp: input.floatProp
        },
        {
            PartitionKey: pk,
            RowKey: String(rk + 1),
            stringProp: 'Amy',
            intProp: 456,
            boolProp: true,
            guidProp: 'd7cb566c-a0b2-433e-8429-4ffcfcef1942',
            floatProp: 687.234
        },
        {
            PartitionKey: pk,
            RowKey: String(rk + 2),
            stringProp: 'Ruby',
            intProp: 789,
            boolProp: false,
            guidProp: 'EC96DC6A-1E9A-4CC7-81F9-649CF8C2E25B',
            floatProp: 987.21
        }
    ];

    context.bindings.items = items;

    context.done();
}