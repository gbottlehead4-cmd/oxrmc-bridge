// Cloudflare Pages Function — /api/hits
// Returns { visits, downloads }.
//   visits    : simple page-load tally stored in KV (this site's own count).
//   downloads : total GitHub release-asset downloads, cached in KV for 10 min
//               so visitors never hit GitHub's API directly (avoids rate limits).
//
// Requires a KV namespace bound as COUNTER in the Pages project settings.
// See site/functions/README.md for the one-time dashboard setup.

const REPO = "gbottlehead4-cmd/oxrmc-bridge";

export async function onRequestGet({ env }) {
  const KV = env.COUNTER;

  // --- visit tally (read-modify-write; fine at this traffic level) ---
  let visits = parseInt((await KV.get("visits")) || "0", 10) || 0;
  visits += 1;
  await KV.put("visits", String(visits));

  // --- download total, cached 10 min ---
  let downloads = await KV.get("downloads_cache");
  if (downloads === null) {
    try {
      const res = await fetch(
        `https://api.github.com/repos/${REPO}/releases`,
        { headers: { "User-Agent": "oxrmc-bridge-site", "Accept": "application/vnd.github+json" } }
      );
      const rels = await res.json();
      let n = 0;
      for (const rel of rels) for (const a of (rel.assets || [])) n += a.download_count || 0;
      downloads = String(n);
      // keep the last good value 24h as a fallback if GitHub is later unreachable
      await KV.put("downloads_cache", downloads, { expirationTtl: 600 });
      await KV.put("downloads_last", downloads, { expirationTtl: 86400 });
    } catch (e) {
      downloads = (await KV.get("downloads_last")) || "0";
    }
  }

  return new Response(
    JSON.stringify({ visits, downloads: parseInt(downloads, 10) || 0 }),
    { headers: { "content-type": "application/json", "cache-control": "no-store" } }
  );
}
