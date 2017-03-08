module.exports = function (context, input) {
    context.log('Node.js function triggered with input', input);

    context.bindings.relatedItems = [
        { related: input },
        { related: input },
        { related: input }
    ];

    context.bindings.item = {
        id: input,
        text: "Hello from Node!"
    };

    context.done();
}