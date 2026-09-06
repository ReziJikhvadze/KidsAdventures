import "./lib/error-capture";

import { consumeLastCapturedError } from "./lib/error-capture";
import { renderErrorPage } from "./lib/error-page";
import { buildSitemapXml } from "./lib/sitemap";

type ServerEntry = {
  fetch: (request: Request, env: unknown, ctx: unknown) => Promise<Response> | Response;
};

let serverEntryPromise: Promise<ServerEntry> | undefined;

async function getServerEntry(): Promise<ServerEntry> {
  if (!serverEntryPromise) {
    serverEntryPromise = import("@tanstack/react-start/server-entry").then(
      (m) => (m.default ?? m) as ServerEntry,
    );
  }
  return serverEntryPromise;
}

// h3 swallows in-handler throws into a normal 500 Response with body
// {"unhandled":true,"message":"HTTPError"} — try/catch alone never fires for those.
async function normalizeCatastrophicSsrResponse(response: Response): Promise<Response> {
  if (response.status < 500) return response;
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) return response;

  const body = await response.clone().text();
  if (!body.includes('"unhandled":true') || !body.includes('"message":"HTTPError"')) {
    return response;
  }

  console.error(consumeLastCapturedError() ?? new Error(`h3 swallowed SSR error: ${body}`));
  return new Response(renderErrorPage(), {
    status: 500,
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}

/** Worth compressing. Images, video and fonts are already compressed formats and are excluded. */
const COMPRESSIBLE =
  /^(?:text\/|application\/(?:json|xml|javascript|manifest\+json)|image\/svg\+xml)/i;

/**
 * Whether the client will actually take gzip, rather than merely mentioning it.
 *
 * `Accept-Encoding: gzip;q=0` names gzip in order to refuse it, and a header containing the word
 * is not the same as a client that wants the encoding — a substring test reads that refusal as
 * consent and answers with bytes the client has said it will not decode. The quality value is
 * what carries the meaning, so it has to be read.
 *
 * A bare `*` stands for anything not otherwise listed, so it grants gzip unless gzip appears with
 * its own weight. RFC 9110 §12.5.3.
 */
function acceptsGzip(header: string | null): boolean {
  if (!header) return false;

  let wildcard: number | undefined;

  for (const part of header.split(",")) {
    const [rawToken, ...parameters] = part.trim().split(";");
    const token = rawToken.trim().toLowerCase();
    if (token !== "gzip" && token !== "*") continue;

    const q = parameters.map((p) => p.trim().toLowerCase()).find((p) => p.startsWith("q="));
    // No q is q=1: the encoding is wanted. An unparseable one is treated as a refusal rather
    // than guessed at, since sending an encoding that was not agreed is the worse failure.
    const weight = q === undefined ? 1 : Number(q.slice(2));
    const wanted = Number.isFinite(weight) && weight > 0;

    // An explicit gzip entry settles it either way; `*` only stands in when there is none.
    if (token === "gzip") return wanted;
    wildcard = weight;
  }

  return wildcard !== undefined && Number.isFinite(wildcard) && wildcard > 0;
}

/**
 * Gzips what this server renders, which nothing else does.
 *
 * `compressPublicAssets` covers the built files — the bundle, the stylesheet, the fonts — because
 * those exist at build time and Nitro can write a `.gz` beside each one. The SSR document does
 * not exist until it is asked for, and Nitro's node-server has no runtime compression, so it went
 * out raw: 40 KB where 7 KB carries the same page. Over a 210 ms round trip from Tbilisi that is
 * roughly a round trip of pure waiting, on the very first response of the visit.
 *
 * Piped rather than buffered. The document is streamed as React renders it, and reading it into
 * memory to compress it would trade the whole point of streaming for the bytes. `CompressionStream`
 * keeps it a stream; gzip rather than brotli because Node 22 offers no brotli here.
 *
 * Static assets arrive already carrying `content-encoding` from the pre-compressed file, and are
 * left alone — compressing twice would produce bytes no browser can read.
 */
function compressed(request: Request, response: Response): Response {
  if (!response.body) return response;
  if (response.headers.has("content-encoding")) return response;
  if (!acceptsGzip(request.headers.get("accept-encoding"))) return response;
  if (!COMPRESSIBLE.test(response.headers.get("content-type") ?? "")) return response;

  // Below about a packet, the gzip header costs more than the encoding saves. Only checked when
  // the length is known — a streamed response has none, and is the case worth compressing anyway.
  const declaredLength = Number(response.headers.get("content-length"));
  if (Number.isFinite(declaredLength) && declaredLength > 0 && declaredLength < 1024) {
    return response;
  }

  const headers = new Headers(response.headers);
  headers.set("content-encoding", "gzip");
  // No longer true of the body being sent, and a wrong one truncates the page.
  headers.delete("content-length");
  const vary = headers.get("vary") ?? "";
  if (!/\baccept-encoding\b/i.test(vary)) {
    headers.set("vary", vary ? `${vary}, Accept-Encoding` : "Accept-Encoding");
  }

  return new Response(response.body.pipeThrough(new CompressionStream("gzip")), {
    status: response.status,
    statusText: response.statusText,
    headers,
  });
}

export default {
  async fetch(request: Request, env: unknown, ctx: unknown) {
    return compressed(request, await render(request, env, ctx));
  },
};

async function render(request: Request, env: unknown, ctx: unknown): Promise<Response> {
  const url = new URL(request.url);
  if (url.pathname === "/sitemap.xml") {
    return new Response(buildSitemapXml(), {
      headers: { "content-type": "application/xml; charset=utf-8" },
    });
  }

  try {
    const handler = await getServerEntry();
    const response = await handler.fetch(request, env, ctx);
    return await normalizeCatastrophicSsrResponse(response);
  } catch (error) {
    console.error(error);
    return new Response(renderErrorPage(), {
      status: 500,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }
}
