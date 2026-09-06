// @lovable.dev/vite-tanstack-config already includes the following — do NOT add them manually
// or the app will break with duplicate plugins:
//   - tanstackStart, viteReact, tailwindcss, tsConfigPaths, nitro (build-only using cloudflare as a default target),
//     componentTagger (dev-only), VITE_* env injection, @ path alias, React/TanStack dedupe,
//     error logger plugins, and sandbox detection (port/host/strictPort).
// You can pass additional config via defineConfig({ vite: { ... }, etc... }) if needed.
import { defineConfig } from "@lovable.dev/vite-tanstack-config";

/*
  Declared here rather than inline because the wrapper's `nitro` type is narrower than what it
  forwards. It documents the narrowing as deliberate — "Nitro v3 is still pre-RC, so we only
  expose stable build-time knobs" — while the runtime spreads the whole object into nitro()
  (`{ preset: "cloudflare-module", ...userNitroOpts }`), so `compressPublicAssets` does arrive.
  A named object skips TypeScript's excess-property check, which a cast would also do while
  hiding a real mistake in one of the fields the type does know about.
*/
const nitro = {
  // Azure App Service (Node sidecar via ASP.NET reverse proxy)
  preset: "node-server",
  /*
    Nothing in front of this server compresses. Azure App Service does not, and the CDN that
    Nitro's own docs assume would be doing it is not deployed here — so the bundle went out at
    515 KB where it gzips to 156, and the stylesheet at 406 KB where it gzips to 77. Measured
    against production on 2026-09-06: no `content-encoding` on any response.

    This writes a `.gz` and a `.br` beside every compressible built file, and the node-server
    serves whichever the browser asked for. Zero runtime cost — the work happens in CI — and it
    covers the Georgian `.ttf` faces too, which are stored uncompressed by the format and are
    the reason `fonts.css` apologises for not being woff2.

    The SSR document is not a built file and is not covered here; `src/server.ts` gzips that.
  */
  compressPublicAssets: { gzip: true, brotli: true },
  output: {
    // Outside wwwroot so ASP.NET Static Web Assets does not track build output
    dir: "../frontend-dist",
  },
};

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
  nitro,
});
