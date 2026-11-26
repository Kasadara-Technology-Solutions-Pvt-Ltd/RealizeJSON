// Renders assets/branding/png/* from the SVG masters with Playwright Chromium.
// Usage: node build/render-brand.js assets/branding
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
(async () => {
  const dir = process.argv[2];
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const jobs = [
    ['realizejson-icon-small.svg', 16, 16, 'realizejson-16.png'],
    ['realizejson-icon-small.svg', 32, 32, 'realizejson-32.png'],
    ['realizejson-icon.svg', 64, 64, 'realizejson-64.png'],
    ['realizejson-icon.svg', 90, 90, 'realizejson-90.png'],
    ['realizejson-icon.svg', 128, 128, 'realizejson-128.png'],
    ['realizejson-icon.svg', 200, 200, 'realizejson-preview-200.png'],
    ['realizejson-icon.svg', 256, 256, 'realizejson-256.png'],
    ['realizejson-icon.svg', 512, 512, 'realizejson-512.png'],
    ['realizejson-wordmark-light.svg', 1200, 256, 'realizejson-wordmark-light.png'],
    ['realizejson-wordmark-dark.svg', 1200, 256, 'realizejson-wordmark-dark.png'],
  ];
  for (const [svg, w, h, out] of jobs) {
    const content = fs.readFileSync(path.join(dir, svg), 'utf8');
    await page.setViewportSize({ width: w, height: h });
    await page.setContent(`<html><body style="margin:0;background:transparent">${content.replace(/width="\d+" height="\d+"/, `width="${w}" height="${h}"`)}</body></html>`);
    await page.screenshot({ path: path.join(dir, 'png', out), omitBackground: true, clip: { x: 0, y: 0, width: w, height: h } });
  }
  await browser.close();
})();
