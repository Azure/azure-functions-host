module.exports = function (context, input) {
    context.log('Node.js function triggered with input', input);

    context.bindings.relatedItems = [
        { id: input + "-r1", testId: input },
        { id: input + "-r2", testId: input },
        { id: input + "-r3", testId: input }
    ];

    context.bindings.item = {
        id: input,
        text: "Hello from Node!"
    };

    context.done();
}