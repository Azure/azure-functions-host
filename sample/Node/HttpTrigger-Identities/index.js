module.exports = function (context, req) {
    console.dir(context.bindingData.identities);
    var identityString = context.bindingData.identities.map(GetIdentityString).join(";");

    var res = {
        status: 200,
        body: identityString,
        headers: {
            'Content-Type': 'text/plain'
        }
    };

    context.done(null, res);
};

function GetIdentityString(identity) {
    var nameIdentifierType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    var userIdClaim = identity.claims.find(claim => claim.type === nameIdentifierType);
    if (userIdClaim) {
        // user claim
        var nameType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
        var userNameClaim = identity.claims.find(claim => claim.type === nameType);
        return `Identity: (${identity.authenticationType}, ${userNameClaim.value}, ${userIdClaim.value})`;
    } else {
        // key based identity
        var authLevelClaim = identity.claims.find(claim => claim.type === "http://schemas.microsoft.com/2017/07/functions/claims/authlevel");
        var keyIdClaim = identity.claims.find(claim => claim.type === "http://schemas.microsoft.com/2017/07/functions/claims/keyid");
        return `Identity: (${identity.authenticationType}, ${authLevelClaim.value}, ${keyIdClaim.value})`;
    }
}