# Cloudflare Pages Functions

## `/api/hits` — visit + download counter

Powers the small stats line in the site footer (`👁 … visits · ⬇ … downloads`).

- **visits** — a page-load tally stored in Cloudflare KV (this site's own count).
- **downloads** — total GitHub release-asset downloads for the repo, cached in KV
  for 10 minutes so visitors never call GitHub's API directly.

### One-time setup (Cloudflare dashboard)

The function needs a KV namespace bound as **`COUNTER`**:

1. Cloudflare dashboard → **Workers & Pages → KV** → **Create a namespace**
   (name it e.g. `oxrmc-site-counter`).
2. Open the **Pages** project (`oxrmc-bridge`) → **Settings → Functions →
   KV namespace bindings** → **Add binding**.
   - Variable name: `COUNTER`
   - KV namespace: the one from step 1
   - Add it for **Production** (and Preview if you use preview deploys).
3. Redeploy (any push to `master`, or **Deployments → Retry** on the latest).

That's it. Until the binding exists the function errors and the footer line just
stays hidden — the rest of the page is unaffected.

### Notes

- KV limits writes to the same key to ~1/second, so under a burst of simultaneous
  visits the tally may occasionally miss one. Fine for a vanity counter; the
  authoritative visit numbers are in **Cloudflare Web Analytics**.
- The function lives here because `site/` is the Pages project **root directory**,
  so `site/functions/api/hits.js` routes to `/api/hits`.
