module.exports = function (context, req) {
    context.log('Node.js HTTP trigger function invoked!');

    context.res = {
        status: 200,
        body: 'Hello World!'
    };

    context.done();
}
