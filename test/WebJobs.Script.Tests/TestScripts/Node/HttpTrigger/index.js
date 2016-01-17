module.exports = function (context) {
    context.log('Node.js HTTP trigger function processed request ' + context.req.body);

    context.output({
        res: {
            status: 200,
            body: context.req.body
        }
    });

    context.done();
}