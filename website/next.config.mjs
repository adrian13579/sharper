import { readFileSync } from "node:fs";
const project = readFileSync(new URL("../Cargo.toml", import.meta.url), "utf8");
const version = project.match(/^version\s*=\s*"([^"]+)"/m)?.[1];
if (!version) throw new Error("Missing Sharper version in Cargo.toml.");
const basePath = (process.env.PAGES_BASE_PATH ?? "/sharper").replace(/\/$/, "");
/** @type {import('next').NextConfig} */
const config = {
  output: "export",
  basePath,
  trailingSlash: true,
  images: { unoptimized: true },
  env: {
    NEXT_PUBLIC_BASE_PATH: basePath,
    NEXT_PUBLIC_SHARPER_VERSION: version,
  },
};
export default config;
