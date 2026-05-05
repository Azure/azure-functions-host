module.exports = function (context, req) {
    context.res = { status: 200, body: 'ok' };
    context.done();
};
