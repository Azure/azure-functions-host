module.exports = function (context, entityId) {
    if (context.bindings.entity.Id != entityId)
    {
        throw "Failed";
    }
    context.log("ApiHubTableEntityIn:", context.bindings.entity.Id);
    context.done();
}