module.exports = function (context, req) {
    context.log('Node.js HttpTrigger function invoked.');

    context.res = {
        status: 200,
        body: {
            reqBodyType: typeof req.body,
            reqBody: req.body,
            reqHeaders: req.headers
        },
        headers: {
            'test-header': 'Test Response Header'
        }
    };

    context.done();
}