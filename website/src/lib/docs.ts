import { readFileSync } from "node:fs";
import path from "node:path";
export const entries = [
  { slug: "overview", title: "Overview", group: "Get started" },
  { slug: "npm", title: "Installation", group: "Get started" },
  {
    slug: "custom-language-servers",
    title: "Server & agent setup",
    group: "Configuration",
  },
  {
    slug: "architecture-rules",
    title: "Custom architecture enforcer",
    group: "Examples",
  },
];
export function getDocuments() {
  return entries.map((entry) => ({
    ...entry,
    content: readFileSync(
      path.join(process.cwd(), "content/docs", `${entry.slug}.md`),
      "utf8",
    ),
  }));
}
