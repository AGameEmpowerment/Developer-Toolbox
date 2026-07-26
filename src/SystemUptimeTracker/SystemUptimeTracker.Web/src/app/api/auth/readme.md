# Authentication and Impersonation

## Local sign-in

The application uses ASP.NET Core Identity with local user accounts. Browser users sign in through `/auth/login`; the web application forwards the resulting secure cookie to the .NET API.

API clients can use the ASP.NET Core Identity bearer-token endpoints. The API can also validate standard JWT bearer tokens when `Auth:Jwt:Enabled` is `true`. JWT validation is local and provider-neutral: configure `Auth:Jwt:Issuer`, `Auth:Jwt:Audience`, and `Auth:Jwt:SigningKey` through user secrets or environment variables. Never commit signing keys.

The user-management tables are created by the `CreateIdentitySchema` Entity Framework migration in `SystemUptimeTracker.Data`. Its `AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`, and `AspNetRoleClaims` tables are the primary user-management store.

## Impersonation

`IMPERSONATING_COOKIE` and `NEXT_PUBLIC_IMPERSONATING_COOKIE` name the cookie where Next.js stores an encrypted identifier for the impersonated account. This should match `AppSettings:ImpersonatingCookie`.

Requests from the client to `/api/*`, excluding auth and strings routes, and requests made using `serverApiGet`, pass the decrypted value to the API in a header with the same name.

The .NET API only honors that header in Development unless the default impersonation authorization service is replaced with an application-specific implementation.

`IMPERSONATE_ENCRYPTION_KEY` is the AES-256-GCM key material for impersonation cookies. A fresh random IV is generated per encrypted payload, so no static IV setting is required. The key is not needed by the API because the API receives the identifier after the Next.js layer decrypts it.

Expected format: 32 random bytes encoded as 64 hexadecimal characters. Generate a local value with:

```powershell
node -e "const crypto=require('node:crypto'); console.log(crypto.randomBytes(32).toString('hex'));"
```
