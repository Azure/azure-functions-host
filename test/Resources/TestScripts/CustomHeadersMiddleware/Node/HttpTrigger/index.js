module.exports = function (context, req) {
    context.log('Node.js HTTP trigger function processed a request.');

    const res = {
        status: 200,
    };

    context.done(null, res);
};
