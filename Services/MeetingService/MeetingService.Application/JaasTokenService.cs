using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace JitsiBackend.API.Application.Common
{
    public class JaasTokenService
    {
        private const string PrivateKeyPem = @"-----BEGIN PRIVATE KEY-----
MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQCmFBNUxkmo+354
N2tOBs5fMMvNyA+29k2Deqqx0AsF63Q2j8TufUuTO3iAKtilaVl2MnWsUQ3Z1Opy
60/fjnrHh5KEBQanZgZVC5lpA0xIsP/wgxmc5lT+ZRrYeGc0SGuQxakwnAcfEqZF
Lj/zGXeOzJQx+rtrQeWihEg1VX9Atf1J2ujkir3I+DL9ouxnLNw33Dt3CRedOG/o
+fjcWhxccooA8VDRS4hl+SrbLCQJ8O1YOBXsviKBNGNgj2RCTMl/GiJjv5Y3d2bk
7MtmS9qSTbgJ6QIyml2Z8T2fe2mtg0GKsoQ3AqCyvok172NfdLxUejuwXNoDRkmn
tgfWGmC/AgMBAAECggEATyaON/7wqCqEEcNHzr4LqO5Wk1JfuvET7C+QHoJqXn3i
uwY18vugAyF6woqpCdx1mJFf56oRkdmZiv9+56q99U41k9nZW/qR1gJbMOMzfglt
UTEKCe70XvHMo5JujUpeiXbKtbIG6tY2jA4IyFFA27vAfTlkDTAwww2MZG7E4Q+W
AK5amxUmZKRUZYJ1G2NuXKCsbdrEE2MKp0/QPeiBe7xvgIFkr69UfD7pElC8Xwq5
xtgK9gSj+rUalhPvgyq5Cmoa3tMwBwUWt4gnUUVnH5+eEd5OXdLrmN87M/12AvQ4
R/mHHfRUP4gLdOfy533HAwhesoYEARomJlO9y5wYkQKBgQDd0bycWAUMkIa/9snH
DwkojxyMTYcyU2pY6QDhuJfuSAJ7uldQ6nnG4YyhP3uUpQjEPYKxfqEXOfAfLUkZ
rCN2ivyLeBIYFSL5Pb1VYBw1wItF5n30Eg4+QwMJnzOMy97JsJKnryQD6PjW6G3a
DbsX45vbbO5Xp+gTjgGicPBoxQKBgQC/q307s8BQ1b0Xef9u9rtAYyvEWmmaSra/
KqN6ASXmYKWIs+51uKiC4qs2FDEWVpIoy+EHy9T/3+dcMGbRNVAwTp//M47QyGiL
QK03bSQNGW/6bc3Eq/hytoYtJ8wd24KCaDtPTFIP+omHP6BjcOXdagnApLLDRyvc
Kvd9N/iTswKBgHOjwgZnxMoWFonBKNRDvGyOnz1ttYvA/PI5FqKWT26Dz/ec36Rp
eO776wqtQ8nsd7OGtbm+4FBxfgNi4nclzJ8iOiKjiBtR+ZeiXjBCGLLVHEZmsUcq
mo5O9Shw/LjsF8th6DLPFaGMNR4kshA5lE4R9NDh6yd7e3umTqfW5R5ZAoGAYkXJ
Bg1Zs5iDRUA16Wz0AUVXsWsx7fwUyttPykRAbGhtNzQaNZ2iOMmDQ00DBhMJCYXP
MTIfWboxY6EldmrBXKNTgYOr2/yFLbDRnzOEYnsCYQJfmFEcJ6TSEuDu1PgxaC+N
CVF2Wd75GLFUyOef0/CEY5OOXHVnVZFqJ3fFKeUCgYB58WbL51bMzfJTOtHQdX30
zTXxE7lk73JfnRY1VH3nib+M/Mnhkgw5JLIYl9yIaau88Fy9IijaTzhj9uesL2Ow
zH3opEXWyCJhlXT3jJKf4BYHDgCyWM4lYL2cf2VtMXVaeoG2nvFSz+TyaxT4lCEo
SAHWJc+6F+fBhsdFHGCYDQ==
-----END PRIVATE KEY-----
";

        private const string KeyId = "vpaas-magic-cookie-e17fdac567914126bc4b82b9f3b4c787/e54407";
        private const string AppId = "vpaas-magic-cookie-e17fdac567914126bc4b82b9f3b4c787";

        public string GenerateToken(
            string roomName = "*",
            string userName = "nguyen.long.ts.bn",
            string userId ="",
            string email = "nguyen.long.ts.bn@gmail.com",
            string avatarUrl = "",
            bool isModerator = true,
            int expiresInMinutes = 120)
        {
            if (string.IsNullOrEmpty(userId))
            {
                userId = $"user-{Guid.NewGuid().ToString("N").Substring(0, 16)}";
            }


            var rsa = RSA.Create();
            rsa.ImportFromPem(PrivateKeyPem.ToCharArray());

            var credentials = new SigningCredentials(
                new RsaSecurityKey(rsa),
                SecurityAlgorithms.RsaSha256
            )
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };

            var header = new JwtHeader(credentials);
            header["kid"] = KeyId; 

            var userContext = new Dictionary<string, object>
            {
                ["id"] = userId,
                ["name"] = userName,
                ["email"] = email,
                ["avatar"] = avatarUrl,
                ["moderator"] = isModerator,
                ["hidden-from-recorder"] = false
            };

            var features = new Dictionary<string, object>
            {
                ["livestreaming"] = isModerator,
                ["recording"] = isModerator,
                ["transcription"] = isModerator,
                ["outbound-call"] = isModerator,
                ["sip-outbound-call"] = false,
                ["file-upload"] = true,
                ["list-visitors"] = false,
                ["flip"] = false
            };

            var context = new Dictionary<string, object>
            {
                ["user"] = userContext,
                ["features"] = features
            };


            var now = DateTime.UtcNow;
            var nbf = now.AddSeconds(-10); 
            var exp = now.AddMinutes(expiresInMinutes);

            var payload = new JwtPayload
            {
   
                { "iss", "chat" },             
                { "aud", "jitsi" },            
                { "sub", AppId },               
                { "room", roomName },
                { "context", context },    
                { "nbf", new DateTimeOffset(nbf).ToUnixTimeSeconds() },
                { "exp", new DateTimeOffset(exp).ToUnixTimeSeconds() },
                { "iat", new DateTimeOffset(now).ToUnixTimeSeconds() }
            };

            var token = new JwtSecurityToken(header, payload);
            var jwtHandler = new JwtSecurityTokenHandler();
            return jwtHandler.WriteToken(token);
        }

 
        public string GetMeetingUrl(string roomName)
        {
            return $"https://8x8.vc/{AppId}/{roomName}";
        }


        public JaasConfig GetConfig()
        {
            return new JaasConfig
            {
                AppId = AppId,
                KeyId = KeyId
            };
        }
    }

    public class JaasConfig
    {
        public string AppId { get; set; }
        public string KeyId { get; set; }
    }
}

