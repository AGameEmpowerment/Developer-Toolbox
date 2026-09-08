# Developer Toolbox example client

This public Next.js sample demonstrates App Router pages, localization,
dynamic routes, linting, formatting, and accessibility-focused unit tests. It
uses a local example catalog so builds and tests do not depend on a private API.

```powershell
npm install
npm run verify
npm run build
```

Use `npm run test:coverage` for a local coverage report, set `REPORT=true`
when a CI job needs JUnit output, or run `npm run upgrade` to review public npm
package updates interactively.

Copy `.env.local.example` to `.env.local` only when experimenting with the
optional public example setting. Build the local container with:

```powershell
docker build --tag developer-toolbox-example-client .
```

The project is a local-development template and must be reviewed and adapted
before production use.
