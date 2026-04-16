module.exports = function (context, input, entity1, entity2) {
    // v5 Tables extension returns JArray even for single-entity lookups
    var e1 = Array.isArray(entity1) ? entity1[0] : entity1;
    var e2 = Array.isArray(entity2) ? entity2[0] : entity2;
    var result = e1.Name + ', ' + e2.Name;
    context.done(null, result);
}