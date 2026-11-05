# Sharper website

Next.js/TypeScript static landing page at https://adrian13579.github.io/sharper/.

With Node 24, run npm ci --ignore-scripts, npm run dev, npm run build and npm run preview from this directory. Append /sharper/ to the printed local URL. PAGES_BASE_PATH overrides the default project path for other hosting.

The build reads Cargo.toml for the current version and copies canonical logos/icons from the repository root/assets. Generated public/brand, .next and out outputs are ignored. The GitHub Pages workflow builds/tests pull requests and deploys out/ on main; Pages source is GitHub Actions.

Run npx playwright install chromium and npm test, or set PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH to an installed Chromium/Edge executable. Tests cover base-path assets, docs, live-stat rendering/failure handling, theme persistence, mobile navigation, responsive layout and automated light/dark accessibility. Screenshots go under ../artifacts/website.

Lead with rapid rule feedback and the check, fix, verify loop. The landing page stays hero-only; detailed guidance lives in the docs. Light/dark themes use neutral surfaces and teal accents with the original dimensional logo.

## Documentation

Canonical guides are Markdown in `content/docs/`. `src/lib/docs.ts` defines sidebar groups and ordering; static routes render every entry under `/docs/`. The shared documentation shell provides full-text search, section anchors, theme controls and previous/next links. Update guides here rather than adding duplicate root docs. Run the browser tests to check internal links, search and responsive navigation.
