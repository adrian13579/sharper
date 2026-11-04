# Sharper branding

Sharper retains its original folded-ribbon S and lowercase `sharper` lettering, including the softly shaded, dimensional (neomorphic) treatment. The palette is neutral charcoal on light backgrounds and silver/off-white on dark backgrounds. The flat/redrawn alternatives are not used.

## Assets and themes

| Asset | Use |
| --- | --- |
| `logo.svg` | README wordmark; self-contained embedded raster with grayscale styling and a `prefers-color-scheme: dark` inversion |
| `logo.png` | Original-layout neutral charcoal master/fallback, 2172 x 724 with transparency |
| `logo-light.png` | Charcoal wordmark and S for light backgrounds |
| `logo-dark.png` | Silver/off-white wordmark and S for dark backgrounds |
| `assets/mcp-icon.png` | Neutral original S master, 128 x 128 |
| `assets/mcp-icon-light.png` | Embedded charcoal MCP icon, theme `light` |
| `assets/mcp-icon-dark.png` | Embedded silver/off-white MCP icon, theme `dark` |

The SVG contains its raster data and CSS only: no JavaScript, remote subresources or fonts. Its light/dark styling changes the color treatment without changing contours or alpha. PNG exports are available for fixed-background consumers. Browsers choose the SVG theme from their rendering context; MCP clients choose between the advertised PNG theme variants. Clients control whether and how icons are displayed.

## Source and export

The accepted neutral masters were edited from the existing raster artwork with the built-in imagegen tool. The S silhouette, letterforms, spacing and original horizontal composition are retained. Chromium renders the theme styling into PNGs so both variants use the same source geometry and transparency. PNG previews must be inspected on opaque light/dark backgrounds; an image viewer that exposes RGB beneath near-transparent pixels can exaggerate otherwise invisible edge pixels.

Accepted wordmark prompt:

> Precise recolor of the provided existing Sharper logo raster. Preserve the exact folded-ribbon S silhouette and the exact lowercase text 'sharper', existing rounded bold letterforms, kerning, proportions, icon-to-wordmark spacing, alignment, margins and transparent canvas. Change only the color treatment. Remove purple/violet and other chromatic hues. Use a neutral monochrome palette, near-black (#171717) wordmark and near-black/grayscale folded S, with subtle neutral-gray folded shading for white/light backgrounds. No new symbols, text, borders, backgrounds or glow. Horizontal 3:1 logo, original 2172 x 724 dimensions.

Accepted S master prompt:

> Recolor the supplied standalone Sharper folded-ribbon S icon. Preserve its exact silhouette, folds, proportions, square composition and even padding. Replace purple/violet with near-black and neutral dark-gray folded shading for light backgrounds. No text, outline, other symbols, glow or colored pixels. Genuine transparent background.

Regenerate SVG/theme exports using Node.js with an installed Chromium/Edge executable:

```sh
node scripts/build-branding.mjs /path/to/chrome-or-edge
```

The script exports the wordmark variants, 128 x 128 RGBA MCP icons and an opaque light/dark comparison at `artifacts/branding/preview.png`. Its browser profile stays under ignored `artifacts/branding`; it does not require npm packages or API credentials. Inspect that preview before committing assets. The README uses Markdown image syntax, retaining compatibility with the package README renderer.
