# Brand guidelines

## Identity

| Element | Value |
|---|---|
| Product name | RealizeJSON (exact capitalization) |
| Marketplace display name | RealizeJSON - Live JSON Serialization Preview |
| Primary tagline | See your JSON before your code runs. |
| Positioning | Real-time serialization intelligence for .NET developers. |
| Short description | Visualize how your C# types serialize into JSON in real time, without building or running your application. |
| Namespaces / projects | `RealizeJSON.*` |

The tagline is used in the README and promotional material only. IDE chrome stays minimal (window caption
"RealizeJSON"). The tagline must never be used to imply that static previews are guaranteed runtime output; every
preview carries its *Static preview* label.

Domain availability and trademark clearance have **not** been verified.

## Palette

| Token | Hex | Use |
|---|---|---|
| Midnight | `#142C4C` | Icon background, "Realize" on light backgrounds |
| Electric Blue | `#3089E8` | "JSON" in the wordmark, promotional highlights |
| Cyan | `#38BDF8` | Lightning bolt (live preview) |
| Slate | `#64748B` | Secondary text on light backgrounds only |

Brand colors are never applied to Visual Studio UI: the tool window uses IDE theme resource keys and editor
classification colors so that light, dark and high-contrast themes are respected.

## Contrast (WCAG 2.x)

| Pair | Ratio | Assessment |
|---|---|---|
| White braces on Midnight | 14.05:1 | AAA |
| Cyan bolt on Midnight | 6.56:1 | AA (graphics ≥ 3:1) |
| Midnight "Realize" on white | 14.05:1 | AAA |
| Electric Blue "JSON" on white | 3.58:1 | AA for large/bold text only (wordmark use) |
| White "Realize" on VS dark `#1E1E1E` | 16.67:1 | AAA |
| Electric Blue "JSON" on VS dark `#1E1E1E` | 4.66:1 | AA |
| Slate on white | 4.76:1 | AA |
| Slate on Midnight | 2.95:1 | Fails — do not use Slate on Midnight |
| Midnight tile edge vs. VS dark `#1E1E1E` | 1.19:1 | Tile edge blends on dark backgrounds; the white braces (14:1) carry recognition |

## Assets (`assets/branding/`)

| File | Description |
|---|---|
| `realizejson-icon.svg` | Master icon (128 grid): rounded-square Midnight tile, white braces, Cyan bolt |
| `realizejson-icon-small.svg` | Small-size master (32 grid) with heavier strokes for 16 and 32 px |
| `realizejson-wordmark-light.svg` | Horizontal wordmark for light backgrounds ("Realize" Midnight, "JSON" Electric Blue) |
| `realizejson-wordmark-dark.svg` | Horizontal wordmark for dark backgrounds ("Realize" white) |
| `png/realizejson-{16,32,64,90,128,256,512}.png` | Raster icons (16/32 from the small master) |
| `png/realizejson-preview-200.png` | VSIX preview image (200 × 200) |
| `png/realizejson-wordmark-{light,dark}.png` | 1200 × 256 wordmark rasters |

The VSIX uses `RealizeJSON-128.png` as the extension icon and `RealizeJSON-preview-200.png` as the preview image. The
tool window and commands use the built-in `KnownMonikers.JSONScript` image so that the IDE renders theme-correct,
DPI-aware glyphs.

Rasters are rendered from the SVG masters with Chromium (see `build/render-brand.js`). The wordmark SVGs use live text
with the font stack `Segoe UI, Inter, Helvetica Neue, Arial`; convert text to outlines in a design tool before using
the SVG in print or contexts where fonts may be substituted. Committed PNGs were rendered with Inter Bold.

## Usage rules

- Flat design only: no gradients, shadows or 3D effects.
- Keep clear space of at least one brace width around the icon.
- Do not recolor, rotate or stretch the icon; do not place the light wordmark on dark backgrounds or vice versa.
- Always write "RealizeJSON" — never "RealizeJson", "Realize JSON" or "realizejson" in prose.

## Verification still required

- Confirm current Visual Studio Marketplace icon/screenshot size requirements at publishing time (the VSIX manifest
  icon is 128 × 128; Marketplace recommendations were not re-verified for this release).
- Visual check of the icon in Extension Manager at 100%/150%/200% DPI in light, dark and high-contrast themes.
