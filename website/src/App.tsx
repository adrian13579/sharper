"use client";

import { useState } from "react";
import { Github, ThemeIcon } from "./Icons";
import ProjectStats from "./ProjectStats";
import { useTheme } from "./useTheme";

const repository = "https://github.com/adrian13579/sharper";
const docs = (file: string) =>
  `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/docs/${file}/`;
const asset = (name: string) =>
  `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/brand/${name}`;
function Arrow() {
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
      <path d="M4 12h15m-6-6 6 6-6 6" />
    </svg>
  );
}
export default function App() {
  const { theme, toggleTheme } = useTheme();
  const [menuOpen, setMenuOpen] = useState(false);
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
            onClick={toggleTheme}
          >
            <ThemeIcon theme={theme} />
          </button>
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
              Give your agent actionable rule findings after each edit. Warm LSP
              sessions keep feedback fast and focused, with less work than
              repeated full builds or test runs. Bring your rules through a{" "}
              <a className="hero-rule-link" href={docs("architecture-rules")}>
                custom LSP enforcer
              </a>
              .
            </p>
            <div className="hero-buttons">
              <a className="button" href={docs("npm")}>
                Get started <Arrow />
              </a>
              <a className="text-link" href={repository}>
                <Github /> Explore on GitHub
              </a>
            </div>
            <ProjectStats />
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
          <a href={`${repository}/blob/main/CONTRIBUTING.md`}>Contribute</a>
          <a href={`${repository}/blob/main/LICENSE`}>Apache 2.0</a>
          <span>Source {process.env.NEXT_PUBLIC_SHARPER_VERSION}</span>
        </div>
      </footer>
    </>
  );
}
