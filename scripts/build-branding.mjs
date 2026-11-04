// Export neutral theme variants through Chromium, preserving raster alpha and contours.
// Usage: node scripts/build-branding.mjs /path/to/chrome-or-edge
import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const browser = process.argv[2];
if (!browser) throw new Error('Pass a Chromium/Edge executable path.');
const assets = path.join(root, 'assets');
const output = path.join(root, 'artifacts/branding');
const profile = path.join(output, 'browser-' + Date.now());
await mkdir(profile, { recursive: true });
const logo = await readFile(path.join(root, 'logo.png'));
const icon = await readFile(path.join(assets, 'mcp-icon.png'));
const width = logo.readUInt32BE(16), height = logo.readUInt32BE(20);
const uri = (data, mime = 'image/png') => `data:${mime};base64,${data.toString('base64')}`;
const svg = (data, w, h, adaptive) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img" aria-label="Sharper"><title>Sharper</title><style>image{filter:grayscale(1)}${adaptive ? '@media(prefers-color-scheme:dark){image{filter:grayscale(1) invert(1)}}' : ''}</style><image width="${w}" height="${h}" href="${uri(data)}"/></svg>\n`;
const logoSvg = svg(logo, width, height, true);
await writeFile(path.join(root, 'logo.svg'), logoSvg);
const child = spawn(browser, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore', windowsHide: true });
let socket, closeBrowser;
try {
    let port;
    for (let attempt = 0; attempt < 100; attempt++) {
        try { port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]); break; }
        catch { await new Promise(resolve => setTimeout(resolve, 100)); }
    }
    if (!port) throw new Error('Chromium did not start within 10 seconds.');
    const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    let sequence = 0;
    const pending = new Map();
    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (message.id && pending.has(message.id)) {
            const { resolve, reject, timer } = pending.get(message.id);
            clearTimeout(timer); pending.delete(message.id);
            if (message.error) reject(new Error(JSON.stringify(message.error))); else resolve(message.result);
        }
    });
    const call = (method, params = {}) => new Promise((resolve, reject) => {
        const id = ++sequence;
        const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Timed out: ${method}`)); }, 15000);
        pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
    });
    closeBrowser = () => call('Browser.close');
    await call('Page.enable');
    await call('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
    const frame = (await call('Page.getFrameTree')).frameTree.frame.id;
    async function render(source, w, h, theme, destination) {
        await call('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
        await call('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: theme }] });
        await call('Page.setDocumentContent', { frameId: frame, html: `<html style="color-scheme:${theme}"><style>html,body{margin:0;background:transparent}img{display:block;width:${w}px;height:${h}px}</style><img src="${source}"></html>` });
        const decoded = await call('Runtime.evaluate', { expression: 'document.images[0].decode().then(()=>true)', awaitPromise: true, returnByValue: true });
        if (decoded.exceptionDetails) throw new Error('Image decoding failed.');
        const screenshot = await call('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: w, height: h, scale: 1 } });
        await writeFile(destination, Buffer.from(screenshot.data, 'base64'));
    }
    await render(uri(Buffer.from(logoSvg), 'image/svg+xml'), width, height, 'light', path.join(root, 'logo-light.png'));
    await render(uri(Buffer.from(logoSvg), 'image/svg+xml'), width, height, 'dark', path.join(root, 'logo-dark.png'));
    const iconSvg = svg(icon, 128, 128, true);
    await render(uri(Buffer.from(iconSvg), 'image/svg+xml'), 128, 128, 'light', path.join(assets, 'mcp-icon-light.png'));
    await render(uri(Buffer.from(iconSvg), 'image/svg+xml'), 128, 128, 'dark', path.join(assets, 'mcp-icon-dark.png'));
    await call('Emulation.setDeviceMetricsOverride', { width: 1080, height: 760, deviceScaleFactor: 1, mobile: false });
    const light = uri(await readFile(path.join(root, 'logo-light.png')));
    const dark = uri(await readFile(path.join(root, 'logo-dark.png')));
    await call('Page.setDocumentContent', { frameId: frame, html: `<style>html,body{margin:0}section{height:380px;display:flex;align-items:center;justify-content:center}img{width:1040px;height:auto}</style><section style="background:#fff"><img src="${light}"></section><section style="background:#0d1117"><img src="${dark}"></section>` });
    await call('Runtime.evaluate', { expression: 'Promise.all(Array.from(document.images, image=>image.decode())).then(()=>true)', awaitPromise: true, returnByValue: true });
    const preview = await call('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: 1080, height: 760, scale: 1 } });
    await writeFile(path.join(output, 'preview.png'), Buffer.from(preview.data, 'base64'));
    console.log('Exported adaptive SVG, neutral logo PNGs and 128x128 themed MCP icons.');
} finally {
    try { await closeBrowser?.(); } catch { }
    socket?.close(); if (child.exitCode === null) child.kill();
}
