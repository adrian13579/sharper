"use client";
import { useEffect, useState, type ReactNode } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
type Document = { slug: string; title: string; group: string; content: string };
const base = process.env.NEXT_PUBLIC_BASE_PATH || "";
const href = (slug: string) => `${base}/docs/${slug}/`;
function anchor(text: string) {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s-]/g, "")
    .trim()
    .replace(/\s+/g, "-");
}
function textOf(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(textOf).join("");
  return "";
}
export default function Docs({
  documents,
  slug,
}: {
  documents: Document[];
  slug: string;
}) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [dark, setDark] = useState(false);
  useEffect(() => {
    let theme = window.matchMedia("(prefers-color-scheme: dark)").matches;
    try {
      const saved = localStorage.getItem("sharper-theme");
      if (saved) theme = saved === "dark";
    } catch {}
    setDark(theme);
    document.documentElement.dataset.theme = theme ? "dark" : "light";
  }, []);
  function toggle() {
    const next = !dark;
    setDark(next);
    document.documentElement.dataset.theme = next ? "dark" : "light";
    try {
      localStorage.setItem("sharper-theme", next ? "dark" : "light");
    } catch {}
  }
  const index = documents.findIndex((doc) => doc.slug === slug);
  const doc = documents[index];
  const sections = Array.from(doc.content.matchAll(/^## (.+)$/gm), (m) => m[1]);
  const words = query.toLowerCase().trim().split(/\s+/).filter(Boolean);
  const results = words.length
    ? documents.filter((doc) =>
        words.every((word) =>
          (doc.title + " " + doc.content).toLowerCase().includes(word),
        ),
      )
    : [];
  return (
    <div className="docs-shell">
      <a className="skip-link" href="#docs-main">
        Skip to content
      </a>
      <header className="docs-header">
        <a href={`${base}/`} aria-label="Sharper home">
          <img
            src={`${base}/brand/logo-${dark ? "dark" : "light"}.png`}
            width="135"
            height="45"
            alt="Sharper"
          />
        </a>
        <span className="docs-header-label">Documentation</span>
        <div className="docs-header-actions">
          <a href="https://github.com/adrian13579/sharper">GitHub</a>
          <button
            onClick={toggle}
            aria-label={`Switch to ${dark ? "light" : "dark"} theme`}
          >
            {dark ? "Light" : "Dark"}
          </button>
          <button
            className="docs-menu"
            aria-expanded={open}
            aria-controls="docs-sidebar"
            onClick={() => setOpen(!open)}
          >
            Sections
          </button>
        </div>
      </header>
      <div className="docs-grid">
        <aside
          id="docs-sidebar"
          className={`docs-sidebar ${open ? "is-open" : ""}`}
        >
          <label htmlFor="docs-search">Search documentation</label>
          <input
            id="docs-search"
            type="search"
            placeholder="Search guides, tools, rules…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {words.length > 0 ? (
            <div className="docs-search-results">
              <p role="status">
                {results.length} {results.length === 1 ? "result" : "results"}
              </p>
              {results.map((result) => {
                const at = result.content.toLowerCase().indexOf(words[0]);
                const snippet = result.content
                  .slice(Math.max(0, at - 45), at + 150)
                  .replace(/[#`*]/g, "");
                return (
                  <a key={result.slug} href={href(result.slug)}>
                    <strong>{result.title}</strong>
                    <span>{snippet}…</span>
                  </a>
                );
              })}
              {results.length === 0 && (
                <p>Try a tool name, rule or configuration setting.</p>
              )}
            </div>
          ) : (
            <nav aria-label="Documentation sections">
              {Array.from(new Set(documents.map((doc) => doc.group))).map(
                (group) => (
                  <div className="docs-nav-group" key={group}>
                    <h2>{group}</h2>
                    {documents
                      .filter((doc) => doc.group === group)
                      .map((doc) => (
                        <a
                          key={doc.slug}
                          href={href(doc.slug)}
                          aria-current={doc.slug === slug ? "page" : undefined}
                        >
                          {doc.title}
                        </a>
                      ))}
                  </div>
                ),
              )}
            </nav>
          )}
        </aside>
        <main id="docs-main" className="docs-main">
          <div className="docs-breadcrumb">
            Documentation <span>/</span> {doc.group}
          </div>
          <article className="docs-article">
            <Markdown
              remarkPlugins={[remarkGfm]}
              components={{
                h2: ({ children }) => (
                  <h2 id={anchor(textOf(children))}>{children}</h2>
                ),
                a: ({ href: target, children }) => {
                  const match = target?.match(/^([a-z0-9-]+)\.md(#[^ ]+)?$/);
                  return (
                    <a
                      href={match ? href(match[1]) + (match[2] || "") : target}
                    >
                      {children}
                    </a>
                  );
                },
                pre: ({ children }) => <pre tabIndex={0}>{children}</pre>,
              }}
            >
              {doc.content}
            </Markdown>
          </article>
          <nav className="docs-pagination" aria-label="Page navigation">
            {index > 0 ? (
              <a href={href(documents[index - 1].slug)}>
                <span>Previous</span>
                {documents[index - 1].title}
              </a>
            ) : (
              <span />
            )}
            {index < documents.length - 1 && (
              <a href={href(documents[index + 1].slug)}>
                <span>Next</span>
                {documents[index + 1].title}
              </a>
            )}
          </nav>
          <footer className="docs-footer">
            Rapid feedback. Your languages. Your rules.
          </footer>
        </main>
        <aside className="docs-toc">
          <nav aria-label="On this page">
            <h2>On this page</h2>
            {sections.map((section) => (
              <a key={section} href={`#${anchor(section)}`}>
                {section}
              </a>
            ))}
          </nav>
        </aside>
      </div>
    </div>
  );
}
