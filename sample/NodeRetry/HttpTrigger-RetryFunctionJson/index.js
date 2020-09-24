var invocationCount = 0;

module.exports = async function (context, req) {
    const reset = req.query.reset;
    invocationCount = reset ? 0 : invocationCount

    context.log('JavaScript HTTP trigger function processed a request.invocationCount: ' + invocationCount);

    invocationCount = invocationCount + 1;
    const responseMessage = "invocationCount: " + invocationCount;
    if (invocationCount < 4) {
        throw new Error('An error occurred');
    }
    context.res = {
        // status: 200, /* Defaults to 200 */
        body: responseMessage
    };
}