# API routes

This is set up to handle the majority of the api routes without additional configuration needed. If you need to customize to another pass-through, you can look at documentation. These two spots are of particular use:

https://nextjs.org/docs/app/building-your-application/routing/route-handlers

https://nextjs.org/docs/app/building-your-application/routing/dynamic-routes

## Authentication Note!:

Any calls to this app's api from a server component will **not** have the information needed to get the access token and pass that through to the front end.

If a server page needs to call the actual .net api, it should call serverApiGetAsJson. This method can accept a zod schema, and a cache parameter. The URl is a required value on the object passed in.

You shouldn't route through to these api routes from server pages. It works if those routes don't require authorization headers, but for consistency your server should always do a server-side-api-fetch

## Zod Validators

We encourage you to use the same zod validators that should drive your UI data validation in-browser as a server-side validator in the pass-through. This is a good step in protecting against users bypassing client validation.

**getZodDefinitionForRequest**: You can update this method to give you a zod schema object that the route.js will use to validate the data.

### Zod tips

**coerce**: z.coerce.number() will make it so zod will accept '2' and coerce it to be numeric
**refine**: look up refine in zod's documentation for opportunities to do things like async check for unique and other potentially useful things.
**optional**: you can make something optional with optional().

## Caching

The route.js will not cache, meaning that any time a request actually reaches the nextjs api route it will go to the API for the data. You can then control how long nextjs will cache your requests in the service by using a util getFetchCacheParameters. Typically, you can pass the result of that object in as the second parameter on your fetch. Alternatively, you can spread it such as:
fetch("myurl",{method:"PATCH", ...getFetchCacheParameters(10)}); That would do a patch telling nextjs to cache it for 10 minutes.

## Environment

nextJs doesn't like trying to talk to our api on https. In the api project's launch settings.json file you should see an https and http url for the api to launch at. That value needs to be in your .env file as the API_BASE_URL variable.

Server side fetches depend on BASE_URL to have the full path it can request to.

## Verification

When you add a passthrough route that accepts request bodies, verify it in three places:

1. The browser or calling component should show the same validation behavior you expect from the UI.
2. The Next.js passthrough route should execute the matching zod validation before proxying the request.
3. The ASP.NET Core API should receive the sanitized payload only after the validation passes.

If you need to debug the path end to end, set breakpoints in both the Next.js passthrough route and the target API endpoint so you can confirm where validation or forwarding stops.

## Authentication

Out of the box we support anonymous and signed-in API passthrough. If you know your app will only ever be doing calls after Microsoft sign-in,
you can remove the try catch surrounding the access token bit at the start of route.js and just call auth.getAccessToken

## Trace IDs And Generic Errors

The passthrough route is also the main frontend boundary for error scrubbing and trace correlation.

- Successful and failed responses should preserve the backend `X-Trace-Id` header when present.
- If the route fails before an upstream response is available, it should generate a local trace ID and include it in the scrubbed response.
- Detailed exception information should only be written to server-side logs and telemetry.
- Browser callers should receive generic payloads with the same `traceId` so support can search backend telemetry for the matching failure.

This keeps the user-facing response safe while preserving one stable identifier from the browser all the way to the backend logs.
