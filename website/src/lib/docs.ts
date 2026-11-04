import { readFileSync } from "node:fs";
import path from "node:path";
export const entries = [
  { slug: "overview", title: "Overview", group: "Get started" },
  { slug: "npm", title: "Installation", group: "Get started" },
  { slug: "agent-setup", title: "Connect your agent", group: "Get started" },
  { slug: "feedback", title: "Check, fix, verify", group: "Workflow" },
  {
    slug: "custom-language-servers",
    title: "Language servers & rules",
    group: "Configuration",
  },
  {
    slug: "architecture-rules",
    title: "Project architecture rules",
    group: "Configuration",
  },
  { slug: "code-navigation", title: "Code navigation", group: "Reference" },
  { slug: "lsp-service", title: "Service contract", group: "Reference" },
  { slug: "compatibility", title: "Compatibility", group: "Reference" },
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
