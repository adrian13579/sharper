"use client";

import { useLayoutEffect, useState } from "react";

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
function Github() {
  return (
    <svg
      width="19"
      height="19"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M12 .8A11.2 11.2 0 0 0 8.46 22.63c.56.1.77-.24.77-.54v-2.1c-3.14.69-3.8-1.33-3.8-1.33-.51-1.3-1.25-1.65-1.25-1.65-1.03-.7.08-.69.08-.69 1.14.08 1.74 1.17 1.74 1.17 1.02 1.74 2.67 1.24 3.32.95.1-.74.4-1.24.72-1.53-2.51-.28-5.15-1.26-5.15-5.58 0-1.23.44-2.23 1.16-3.02-.12-.28-.5-1.43.11-2.98 0 0 .95-.3 3.08 1.15a10.7 10.7 0 0 1 5.6 0c2.14-1.45 3.08-1.15 3.08-1.15.62 1.55.23 2.7.12 2.98.72.79 1.15 1.79 1.15 3.02 0 4.33-2.64 5.29-5.16 5.57.4.35.77 1.03.77 2.08v3.16c0 .3.2.65.78.54A11.2 11.2 0 0 0 12 .8Z" />
    </svg>
  );
}
function ThemeIcon({ theme }: { theme: Theme }) {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      {theme === "light" ? (
        <path d="M20.7 13.2A8.8 8.8 0 0 1 10.8 3.3a9 9 0 1 0 9.9 9.9Z" />
      ) : (
        <>
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5" />
        </>
      )}
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
          <a href={docs("feedback")} onClick={() => setMenuOpen(false)}>
            How it works
          </a>
          <a href={docs("overview")} onClick={() => setMenuOpen(false)}>
            Documentation
          </a>
          <a href={repository}>
            GitHub <Arrow diagonal />
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
            <a className="release-label" href={docs("overview")}>
              <span className="status-dot" /> RAPID RULE FEEDBACK{" "}
              <Arrow diagonal />
            </a>
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
              for your project's architecture rules, so it can check, fix and
              verify as it works.
            </p>
            <div className="hero-buttons">
              <a className="button" href={docs("npm")}>
                Start building <Arrow />
              </a>
              <a className="text-link" href={repository}>
                <Github /> Explore on GitHub
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
