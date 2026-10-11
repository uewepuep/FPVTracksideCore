// FPVTrackside race-stage UI. A client-only Nuxt UI app, generated to static
// files in ../Webb/Stages/UI, which Webb embeds and serves at /stages. It
// talks to FPVTrackside only through the REST API (/api/v1), so the same app
// works in dev (proxied to a running FPVTrackside) and embedded.
//
// Everything it needs ships with it — icons and fonts are bundled at build
// time — because a venue laptop is often offline.
export default defineNuxtConfig({
  compatibilityDate: '2026-09-01',
  modules: ['@nuxt/ui'],
  ssr: false,
  devtools: { enabled: false },
  telemetry: false,
  css: ['~/assets/css/main.css'],

  app: {
    baseURL: '/stages/',
    head: {
      title: 'Stages · FPVTrackside',
      meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
    },
  },

  colorMode: {
    preference: 'dark',
  },

  fonts: {
    families: [
      { name: 'Inter', provider: 'google', weights: [400, 500, 600, 700] },
      { name: 'Chakra Petch', provider: 'google', weights: [500, 600, 700] },
      { name: 'JetBrains Mono', provider: 'google', weights: [400, 600] },
    ],
  },

  icon: {
    provider: 'none',
    clientBundle: { scan: true },
  },

  nitro: {
    output: { publicDir: '../Webb/Stages/UI' },
  },

  vite: {
    server: {
      // `npm run dev`: talk to FPVTrackside (or the ApiSandbox) on :8080.
      proxy: { '/api': process.env.FPV_API || 'http://localhost:8080' },
    },
  },
})
