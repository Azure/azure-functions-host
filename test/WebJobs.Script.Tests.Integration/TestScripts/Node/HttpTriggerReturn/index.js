module.exports = function (context, req) {
    context.res.type("text/plain").send("test");
}