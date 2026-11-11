# Holm website

Holm’s product website lives here, separately from the WPF application. It includes the overview, features, downloads, ten guide articles, about, GitHub release history and a useful 404 page. Public content and downloads render as ordinary HTML before JavaScript runs.

## Run locally

Use Node.js 22 or newer (CI uses Node 24). From the repository root:

```powershell
cd website
npm ci
npm run dev
```

Open the printed URL: `http://127.0.0.1:4321/`. The server binds only to this computer. There is no hot reload: rebuild and refresh after editing.

```powershell
npm run check
npm run lint
npm run build
npm test
npm run preview
```

`npm run format` formats the source. Prettier is the only development dependency. The generated site has **no production dependencies**, framework runtime, remote fonts, analytics or third-party scripts. Do not commit `dist/`, `node_modules/` or QA output.

## Hosting recommendation: GitHub Pages

GitHub Pages is a good fit for this free, open-source product’s static information site: the source, releases and workflow already live in the same repository, and the site needs no server functions, accounts, database or hosting credentials. Windows downloads stay on GitHub Releases rather than consuming website bandwidth.

The alternatives were considered against this actual site:

| Host | Fit and tradeoff |
| --- | --- |
| **GitHub Pages** | Recommended. Included for public repositories on GitHub Free, custom domains and HTTPS, and GitHub Actions publication. Static hosting only; 1 GB site and a soft 100 GB/month bandwidth limit are ample for these pages. |
| Cloudflare Pages | Strong alternative with global delivery, custom domains and preview deployments. The free plan currently allows 500 builds/month and has a 25 MiB individual asset limit. Requires another hosting account and Git integration; there is no server requirement to justify that additional setup today. |
| Vercel | Can host the static output and provides previews and custom domains. Its free Hobby tier is limited to personal, non-commercial use. Its server framework capabilities offer little benefit here. |
| Netlify | Supports this static output, Git deployments, HTTPS, custom domains and previews. New free plans use a credit allowance, with production deployments and traffic consuming credits. More billing-policy monitoring than this site needs. |

Limits were checked on 11 November 2026. Recheck provider terms before changing hosts: [GitHub limits](https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits), [Cloudflare limits](https://developers.cloudflare.com/pages/platform/limits/), [Vercel Hobby](https://vercel.com/docs/plans/hobby), [Netlify plans](https://docs.netlify.com/manage/accounts-and-billing/billing/billing-for-credit-based-plans/credit-based-pricing-plans/). This comparison does not claim a paid plan will never be needed as traffic grows.

### First publication

The production domain is `https://holmnotch.ch`, registered with Infomaniak. The Website workflow publishes pushes to main and public release updates after all checks pass. Pull requests only validate. Source configuration alone does not establish a successful deployment.

1. Review and commit the website and `.github/workflows/website.yml` on `main`, following the repository co-author convention, then push them.
2. In the repository’s **Settings → Pages**, choose **GitHub Actions** as the build/deployment source. This requires repository administration access.
3. Under **Custom domain**, enter `holmnotch.ch` and save. Then publish with a push to main, or under **Actions → Website → Run workflow**, select main and leave publishing enabled.
4. Approve the `github-pages` environment if the repository requires it. The deploy job publishes only after syntax checks, formatting, the build and all tests succeed.
5. Verify the deployment URL returned by Actions and check that the HTTPS site responds.

The workflow uses the temporary `GITHUB_TOKEN`, `pages: write` and `id-token: write` only where needed. **No personal access token or external secret is required.** Published or edited public GitHub releases refresh and deploy the fallback snapshot automatically. Live release metadata refreshes independently in visitors’ browsers.

### Custom domain or another static host

The build accepts two settings:

| Setting | Default | Purpose |
| --- | --- | --- |
| `SITE_URL` | `https://holmnotch.ch` | Site origin only, without a path. Used by canonical links, Open Graph, robots and sitemap. |
| `BASE_PATH` | `/` | Leading and trailing slash. Used by every internal link and asset. |

Repository variables `HOLM_SITE_URL` and `HOLM_SITE_BASE_PATH` can override these defaults. At Infomaniak, point the apex (@) A records to 185.199.108.153, 185.199.109.153, 185.199.110.153 and 185.199.111.153. Point www with a CNAME to the GitHub account's Pages hostname (for ademerci/Holm: ademerci.github.io). Add the custom domain to GitHub Pages before changing DNS, preserve email records, and enable Enforce HTTPS when the certificate is ready. GitHub Actions publishing reads the custom domain from Pages settings; a CNAME file alone does not configure it.

For another host, build with the appropriate origin and base path, then upload `website/dist/`. Do not configure SPA fallback: these are real page directories, and unknown routes should return `404.html` with HTTP 404. The generated `_headers` file is useful on Cloudflare/Netlify; **GitHub Pages ignores custom HTTP headers**, so those policies are not claimed for Pages. CDN compression and cache settings depend on the selected host. There is no service worker that could hold onto old releases.

## Source map and maintenance

| File | Edit for |
| --- | --- |
| `src/content.mjs` | Guide articles, feature descriptions and known limitations. Validate changes against the app README and actual implementation. |
| `src/pages.mjs` | Shared navigation, footer, channels, screenshots and page compositions. |
| `src/style.css` | Design tokens, light/dark appearances, breakpoints, components, motion, focus and print styles. |
| `src/client.js` | Mobile menu, real screenshot selection, guide search, appearance and release enhancement. |
| `src/releases.mjs` | Trusted release URL validation, release selection, caching, escaped notes rendering and API fallbacks. |
| `data/releases.json` | Verified public GitHub API snapshot. Refresh; never invent versions, publication dates or assets. |
| `data/store.json` | Official Store URL and optional verified version. Leave null until a real listing is confirmed. |
| `scripts/build.mjs` | Static generation, metadata, sitemap and verified transparent project captures. |
| `scripts/prepare-screenshots.py` | Optional asset-authoring helper: remove the known capture backdrop while preserving opaque UI pixels. Requires Pillow, not used by CI. |
| `scripts/refresh-releases.mjs` | Bounded public API fetch at build time; retains the last verified snapshot if GitHub is unavailable. |

### Releases and downloads

Run `npm run refresh-releases` before building to refresh the most recent 30 public GitHub releases. CI does this automatically with its temporary token. A failed fetch keeps the checked-in verified snapshot and prints its age. API failure does not invent data or prevent an offline build.

The download and changelog pages progressively refresh from GitHub’s official releases API. A successful fetch is cached in local storage for 15 minutes. Denied storage, network errors, empty or malformed responses and rate limits retain usable release information with an explicit fallback message. A cache older than the generated snapshot cannot replace that snapshot. Browser requests use no credentials or secret.

The main download chooses the newest non-prerelease with an exact `Holm.exe` asset. ZIPs are secondary downloads. Prereleases remain visible and labelled in history, with an include/exclude control. Only HTTPS URLs under the official repository’s release paths are accepted. Release notes are escaped before a limited Markdown subset is rendered; raw HTML, remote images and scripts cannot execute.

The verified Store listing is [Holm for Windows by Holm Team](https://apps.microsoft.com/detail/9NZF994ZPFMH), product `9NZF994ZPFMH`. The Store release is **1.9.0**, package **1.9.0.0**, verified on 11 November 2026 against the public listing and published package/submission status. `data/store.json` deliberately tracks this separately from GitHub’s 1.9.5. Review it after each Store publication; do not copy the portable GitHub version into that field. Store-managed updates and Holm’s portable updater remain distinct; the website does not modify either app path.

### Authentic assets

All 35 product captures have transparent derivatives in `src/assets/screenshots/`; originals in `../docs/` remain unchanged. They are screenshots of the actual application, not website-created app interfaces. The known blue desktop matte is removed, with edge coverage recovered where possible. The four hub windows use their captured rounded outline; the stream viewer retains the screen shown inside its window. The comparison graphic keeps its four app captures with the surrounding backdrop and editorial labels removed. No UI text, controls, artwork or opaque interior pixels are redrawn. The helper verifies opaque UI pixels against the originals byte for byte, and the build verifies source/output hashes.

If an original capture changes, install Pillow in your local Python environment if needed, then run `python scripts/prepare-screenshots.py` from this folder. Review the alpha edges on light and dark backgrounds, and commit the generated PNGs and manifest. Python/Pillow is optional for asset authoring; a normal Node build and CI consume the committed PNGs directly.

Captures are presented as images rather than a live app demo, with descriptive alternatives and fixed source dimensions; below-the-fold captures lazy-load. No Apple artwork, website source, fonts or interface assets are redistributed.

The app icons in `src/assets/` are smaller derivatives of `../docs/icon.png`, for efficient delivery. If the brand icon changes, regenerate those three sizes (96, 192 and 512 pixels). Social sharing uses the real expanded player capture. Preserve original product captures; do not redraw controls to imply new functionality.

No essential product screenshots are missing. A confirmed custom domain remains an external input.

## Design and accessibility

The design applies hierarchy, product-led storytelling and restrained materials from [Apple’s product pages](https://www.apple.com/macos/) and official [materials](https://developer.apple.com/design/human-interface-guidelines/materials), [motion](https://developer.apple.com/design/human-interface-guidelines/motion) and [accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility) guidance. The visual identity comes from Holm’s black/white icon and the colour already in its captures. Only the sticky navigation uses blur, with an opaque fallback. There are no continuous animations, scroll-jacking or stock photography.

Typography uses the installed system font stack. Shared tokens define surface, type, spacing, radii and easing. Light and dark appearances follow the system unless changed in the footer. Keyboard focus, a skip link, labelled navigation, real link destinations, semantic headings, native release disclosures, a labelled search and arrow-key screenshot tabs support accessible use. Reduced motion removes transitions and smooth scrolling. Without JavaScript, the guide, releases and downloads remain available and an additional stylesheet keeps mobile navigation visible.

## Verification

`npm test` validates stable/prerelease selection, official destinations, escaped release notes, cache freshness, denied storage, API outages and malformed responses, plus all generated routes, fragments, asset references, image metadata, unique page metadata, root-domain generation, sitemap and payload budgets. `npm run check` checks JavaScript syntax; `npm run lint` checks source formatting. There is no TypeScript compilation because the implementation uses native JavaScript modules rather than a transpiler.

Browser verification covers desktop, tablet and mobile layouts, navigation, screenshot mouse/keyboard selection, live GitHub data, guide search, empty search results and appearance controls. Payload checks are useful regression guards, **not Lighthouse scores**. No Lighthouse or full WCAG certification is claimed. Run an independent Lighthouse/accessibility audit against the deployed HTTPS site before relying on specific scores or real-user Core Web Vitals.
