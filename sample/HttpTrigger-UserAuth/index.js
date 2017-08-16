const util = require('util');

const claimTypes = {
    nameIdentifier: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
    name: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'
}

module.exports = function (context, req) {
    var body,
        identity = context.identity,
        userIdClaim = identity.claims[claimTypes.nameIdentifier];

    if (userIdClaim) {
        var userNameClaim = identity.claims[claimTypes.name];
        body = util.format('Identity: (%s, %s, %s)', identity.authenticationType, userNameClaim.value, userIdClaim.value); 
    }
    else {
        var authLevelClaim = identity.claims['urn:functions:authLevel'],
            keyIdClaim = identity.claims['urn:functions:keyId'];
        body = util.format('Identity: (%s, %s, %s)', identity.authenticationType, authLevelClaim.value, keyIdClaim.value);
    }

    var res = {
        status: 200,
        body: body
    };

    context.done(null, res);
};
