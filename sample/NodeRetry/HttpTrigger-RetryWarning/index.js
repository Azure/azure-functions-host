module.exports = async function (context, req) {
    context.log('JavaScript HTTP trigger function processed a request.');
    if (context.executionContext.retryContext) {
        context.res = {
            status: 500
        };
    } else {
        context.res = {
            body: 'OK'
        };
    }
}