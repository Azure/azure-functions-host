// Mobile table node scripts cannot Update values 
// because output bindings perform an insert rather
// than an upsert.
// So we'll insert with a known id that we can query
// from the test. That'll confirm this was called as
// expected.

module.exports = function (context, input) {    

    context.bindings.itemOut = {
        id: context.bindings.itemIn.id + "-success"
    };

    context.done();
}