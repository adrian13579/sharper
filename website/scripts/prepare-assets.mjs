import { copyFile, mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
const root = fileURLToPath(new URL("../../", import.meta.url));
const destination = path.join(root, "website/public/brand");
await mkdir(destination, { recursive: true });
for (const name of ["logo-light.png", "logo-dark.png"])
  await copyFile(path.join(root, name), path.join(destination, name));
for (const theme of ["light", "dark"])
  await copyFile(
    path.join(root, `assets/mcp-icon-${theme}.png`),
    path.join(destination, `icon-${theme}.png`),
  );
console.log("Copied canonical theme assets.");
