module.exports = function (context, input, entity1, entity2) {
    var result = entity1.Name + ', ' + entity2.Name;
    context.done(null, result);
}