# Holm website

The site at [holmnotch.ch](https://holmnotch.ch), kept as finished static files in `site/`. Edit those files directly.

```powershell
npm ci
npm run build   # copies site/ to dist/
npm test        # developers on About, no blue-to-purple gradients
npm run preview # http://127.0.0.1:4321
```

holmnotch.ch is served by the Cloudflare Worker `holm-website` (static assets only). To publish, from a terminal signed in with `npx wrangler login`:

```powershell
npx wrangler versions upload -c <config naming holm-website with assets.directory = website/dist, not_found_handling 404-page>
npx wrangler versions deploy --version-id <id> --percentage 100 --name holm-website
```

Release lists on the download and changelog pages load live from GitHub in the browser.
