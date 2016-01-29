module.exports = function (context) {
    context.log('GitHub WebHook triggered! ' + context.req.body.comment.body);
    context.done(null, 'New GitHub comment: ' + context.req.body.comment.body);
}