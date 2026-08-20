// @lovable.dev/vite-tanstack-config already includes the following — do NOT add them manually
// or the app will break with duplicate plugins:
//   - tanstackStart, viteReact, tailwindcss, tsConfigPaths, nitro (build-only using cloudflare as a default target),
//     componentTagger (dev-only), VITE_* env injection, @ path alias, React/TanStack dedupe,
//     error logger plugins, and sandbox detection (port/host/strictPort).
// You can pass additional config via defineConfig({ vite: { ... }, etc... }) if needed.
import { defineConfig } from "@lovable.dev/vite-tanstack-config";

export default defineConfig({
  tanstackStart: {
    // Redirect TanStack Start's bundled server entry to src/server.ts (our SSR error wrapper).
    // nitro/vite builds from this
    server: { entry: "server" },
  },
  vite: {
    build: {
      outDir: "dist",
    },
    server: {
      // Some API responses carry same-origin paths rather than absolute URLs — the guest
      // preview returns "/api/adventure-packs/guest-preview/{id}/cover" for its cover, which
      // the browser resolves against whatever origin the page came from. That is right where
      // one host serves both the SPA and the API, and wrong in dev, where Vite is on 8080 and
      // the API on 5080: the image request lands on Vite and comes back as the 404 HTML page.
      //
      // Forwarding /api keeps dev on a single origin, so those paths resolve the same way they
      // do when the API hosts the frontend. Dev-server only — `vite build` ignores it.
      proxy: {
        "/api": {
          target: process.env.API_PROXY_TARGET ?? "http://localhost:5080",
          changeOrigin: true,
        },
      },
    },
  },
  nitro: {
    // Azure App Service (Node sidecar via ASP.NET reverse proxy)
    preset: "node-server",
    output: {
      // Outside wwwroot so ASP.NET Static Web Assets does not track build output
      dir: "../frontend-dist",
    },
  },
});
