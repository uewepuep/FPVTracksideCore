# Stages web app

The page FPVTrackside serves at `/stages`. A Nuxt (Nuxt UI) single-page app;
everything it does goes through FPVTrackside's REST API (`/api/v1`, see
`documentation/REST_API.md`).

The build is embedded in `Webb.dll`, so it must exist before FPVTrackside is
built. Release builds do this automatically; locally:

```bash
cd StagesUI
npm ci
npm run generate   # writes ../Webb/Stages/UI, embedded by Webb.csproj
```

Without it, `/stages` shows a "not built" page and the API still works.

Development with hot reload against a running FPVTrackside (or the API
sandbox: `dotnet run --project ApiSandbox`):

```bash
FPV_API=http://localhost:8080 npm run dev   # http://localhost:3000/stages/
```

`public/art/` holds the illustrations (generated with Nano Banana 2).
