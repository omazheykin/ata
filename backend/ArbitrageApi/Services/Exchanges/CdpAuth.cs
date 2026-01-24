
using System;
using System.Net.Http;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

public static class CdpAuth
{
    // Inputs from your CDP key:
    // apiKeyId example: "organizations/{org_id}/apiKeys/{key_id}"
    // apiSecretPem: your ECDSA private key PEM string with escaped newlines: 
    // "-----BEGIN EC PRIVATE KEY-----\n...your key...\n-----END EC PRIVATE KEY-----\n"
    public static string CreateJwtForRequest(string apiKeyId, string apiSecretPem, string method, string host, string path)
    {
        // 1) Parse ECDSA private key from PEM (ES256)
        //    NOTE: Ensure your key was created with ECDSA if you intend to use ES256.
        //    If you created an Ed25519 key, you'd use EdDSA instead (different signing libs).
        var ecdsa = ECDsa.Create();
        // Import EC private key from PEM
        // .NET’s built-in ImportFromPem is available in recent runtimes:
        ecdsa.ImportFromPem(apiSecretPem);

        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = apiKeyId };
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        // 2) Build JWT with request-bound claims Coinbase expects
        //    (names vary—follow the CDP docs/examples exactly)
        var now = DateTimeOffset.UtcNow;
        var handler = new JwtSecurityTokenHandler();

        var token = new JwtSecurityToken(
            issuer: apiKeyId,               // often set to key id
            audience: host,                 // requestHost (e.g., "api.coinbase.com")
            claims: new[]
            {
                new System.Security.Claims.Claim("requestMethod", method.ToUpperInvariant()),
                new System.Security.Claims.Claim("requestPath",   path),
                new System.Security.Claims.Claim("requestHost",   host),
            },
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(1).UtcDateTime, // short TTL is recommended
            signingCredentials: creds
        );

        return handler.WriteToken(token);
    }
}