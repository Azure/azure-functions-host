const util = require('util');

const claimTypes = {
    nameIdentifier: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
    name: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'
}

module.exports = function (context, req) {
    var body = context.identities.map(identity => getIdentityString(identity)).join(";");

    var resp = {
        status: 200,
        body: body
    };

    context.done(null, resp);
};

function getIdentityString(identity) {
    var userIdClaim = findFirst(identity, claimTypes.nameIdentifier);
    if (userIdClaim) {
        var userNameClaim = findFirst(identity, claimTypes.name);
        body = util.format('Identity: (%s, %s, %s)', identity.auth_typ, userNameClaim.val, userIdClaim.val);
    }
    else {
        var authLevelClaim = findFirst(identity, 'urn:functions:authLevel');
        var keyIdClaim = findFirst(identity, 'urn:functions:keyId');
        body = util.format('Identity: (%s, %s, %s)', identity.auth_typ, authLevelClaim.val, keyIdClaim.val);
    }
    return body;
}

function findFirst(identity, name) {
    for (let claim of identity.claims) {
        if (claim.typ == name) {
            return claim;
        }
    }
}