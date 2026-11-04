"use client";

import { useLayoutEffect, useState } from "react";
import { Github, ThemeIcon } from "./Icons";

const repository = "https://github.com/adrian13579/sharper";
const docs = (file: string) =>
  `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/docs/${file}/`;
const asset = (name: string) =>
  `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/brand/${name}`;
type Theme = "light" | "dark";
function Arrow({ diagonal = false }: { diagonal?: boolean }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      aria-hidden="true"
    >
      <path d={diagonal ? "M6 18 18 6M6 6h12v12" : "M4 12h15m-6-6 6 6-6 6"} />
    </svg>
  );
}
export default function App() {
  const [theme, setTheme] = useState<Theme>("light");
  const [themeReady, setThemeReady] = useState(false);
  useLayoutEffect(() => {
    let preferred: Theme = window.matchMedia("(prefers-color-scheme: dark)")
      .matches
      ? "dark"
      : "light";
    try {
      const stored = localStorage.getItem("sharper-theme");
      if (stored === "light" || stored === "dark") preferred = stored;
    } catch {
      /* Storage is optional. */
    }
    setTheme(preferred);
    setThemeReady(true);
  }, []);
  const [menuOpen, setMenuOpen] = useState(false);
  useLayoutEffect(() => {
    if (!themeReady) return;
    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;
    try {
      localStorage.setItem("sharper-theme", theme);
    } catch {
      /* Theme still works without storage. */
    }
  }, [theme, themeReady]);
  const logo = asset(`logo-${theme}.png`);
  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header container">
        <a className="brand" href="#" aria-label="Sharper home">
          <img src={logo} alt="Sharper" width="150" height="50" />
        </a>
        <nav
          id="mobile-nav"
          className={menuOpen ? "nav-links is-open" : "nav-links"}
          aria-label="Main navigation"
        >
          <a href={docs("overview")} onClick={() => setMenuOpen(false)}>
            Documentation
          </a>
          <a href={repository}>
            <Github /> GitHub
          </a>
        </nav>
        <div className="header-actions">
          <button
            className="theme-toggle"
            type="button"
            aria-label={`Switch to ${theme === "light" ? "dark" : "light"} theme`}
            onClick={() => setTheme(theme === "light" ? "dark" : "light")}
          >
            <ThemeIcon theme={theme} />
          </button>
          <a className="button button-small" href={docs("npm")}>
            Get started <Arrow />
          </a>
          <button
            className="menu-toggle"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="mobile-nav"
            aria-label={menuOpen ? "Close navigation" : "Open navigation"}
            onClick={() => setMenuOpen(!menuOpen)}
          >
            <span />
            <span />
          </button>
        </div>
      </header>
      <main id="main" tabIndex={-1}>
        <section className="hero container" aria-labelledby="hero-title">
          <div className="hero-copy">
            <p className="eyebrow hero-eyebrow">
              FAST FEEDBACK. PROJECT RULES. VERIFIED RESULTS.
            </p>
            <h1 id="hero-title">
              Fast feedback.
              <br />
              <span>Sharper code.</span>
            </h1>
            <p className="hero-description">
              Give your agent rapid feedback on compiler and rule violations.
              Connect a{" "}
              <a className="hero-rule-link" href={docs("architecture-rules")}>
                custom LSP enforcer
              </a>{" "}
              such as one checking project-specific architecture rules, so it
              can check, fix and verify as it works.
            </p>
            <div className="hero-buttons">
              <a className="button" href={docs("npm")}>
                Start building <Arrow />
              </a>
              <a className="text-link" href={repository}>
                <Github /> Explore on GitHub
              </a>
            </div>
            <div className="project-stats" aria-label="Project statistics">
              <a href={`${repository}/stargazers`} aria-label="GitHub stars">
                <img
                  src="https://img.shields.io/github/stars/adrian13579/sharper?style=flat&label=GitHub%20stars&color=6d28d9"
                  alt="GitHub stars"
                  width="130"
                  height="20"
                />
              </a>
              <a
                href="https://www.npmjs.com/package/@adrian13579/sharper"
                aria-label="npm downloads per month"
              >
                <img
                  src="https://img.shields.io/npm/dm/@adrian13579/sharper?style=flat&label=downloads%2Fmonth&color=6d28d9"
                  alt="npm downloads per month"
                  width="175"
                  height="20"
                />
              </a>
            </div>
            <div className="hero-footnote">
              <span>LOCAL MCP SERVER</span>
              <i />
              <span>OPEN SOURCE</span>
              <i />
              <span>APACHE 2.0</span>
            </div>
          </div>
        </section>
      </main>
      <footer className="site-footer container">
        <a className="footer-brand" href="#" aria-label="Sharper home">
          <img src={logo} alt="Sharper" width="120" height="40" />
        </a>
        <p>A sharper edge for your coding agent.</p>
        <div>
          <a href={`${repository}/issues`}>Issues</a>
          <a href={`${repository}/blob/main/AGENTS.md`}>Contribute</a>
          <a href={`${repository}/blob/main/LICENSE`}>Apache 2.0</a>
          <span>Source {process.env.NEXT_PUBLIC_SHARPER_VERSION}</span>
        </div>
      </footer>
    </>
  );
}
