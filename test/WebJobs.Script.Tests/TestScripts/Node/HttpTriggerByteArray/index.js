module.exports = function (context, req) {
    var body = req.body;

    context.log("TestResult:", {
        isBuffer: Buffer.isBuffer(body),
        length: body.length
    });

    context.res = {
        status: 200,
        body: "Success!"
    };

    context.done();
}