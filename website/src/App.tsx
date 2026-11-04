"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";

const repository = "https://github.com/adrian13579/sharper";
const docs = (file: string) => `${repository}/blob/main/docs/${file}.md`;
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

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setError(false);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 2500);
    } catch {
      setError(true);
    }
  }
  return (
    <>
      <button
        className="copy-button"
        type="button"
        onClick={copy}
        aria-label="Copy install commands"
      >
        {copied ? "✓ Copied" : "Copy"}
        <svg
          width="13"
          height="13"
          viewBox="0 0 20 20"
          fill="none"
          stroke="currentColor"
          aria-hidden="true"
        >
          <rect x="6" y="6" width="10" height="11" rx="2" />
          <path d="M12 6V3H3v10h3" />
        </svg>
      </button>
      <span className="copy-feedback" role="status">
        {copied
          ? "Commands copied to clipboard."
          : error
            ? "Clipboard unavailable. Select and copy the commands below."
            : ""}
      </span>
    </>
  );
}

const documents = [
  {
    number: "01",
    tag: "START HERE",
    title: "Connect your agent",
    description:
      "Guided setup, custom profiles and configuration for your preferred client.",
    file: "agent-setup",
  },
  {
    number: "02",
    tag: "EXPLORE THE TOOLS",
    title: "Feedback & rule verification",
    description: "Baseline, edit, check and fix with native project rules.",
    file: "feedback",
  },
  {
    number: "03",
    tag: "KNOW THE CONTRACT",
    title: "How Sharper works",
    description:
      "Session lifecycle, synchronization, scope and freshness guarantees.",
    file: "lsp-service",
  },
  {
    number: "04",
    tag: "BUILD & PACKAGE",
    title: "Native installation",
    description: "Build the Rust executable or use a native release package.",
    file: "native-installation",
  },
  {
    number: "05",
    tag: "SUPPORTED ENVIRONMENTS",
    title: "Compatibility",
    description:
      "Language-server versions, platform targets and upgrade guidance.",
    file: "compatibility",
  },
  {
    number: "06",
    tag: "PROJECT STATUS",
    title: "Production readiness",
    description:
      "The remaining qualification work and what current evidence covers.",
    file: "production-readiness",
  },
];

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
  const [platform, setPlatform] = useState<"windows" | "linux">("windows");
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
  const install = `git clone https://github.com/adrian13579/sharper.git
cd sharper
cargo install --path . --locked --target ${platform === "windows" ? "x86_64-pc-windows-msvc" : "x86_64-unknown-linux-gnu"}
sharper setup`;
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
          <a href="#workflow" onClick={() => setMenuOpen(false)}>
            How it works
          </a>
          <a href="#documentation" onClick={() => setMenuOpen(false)}>
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
          <a className="button button-small" href="#get-started">
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
            <a className="release-label" href={docs("production-readiness")}>
              <span className="status-dot" /> PRE-ALPHA · NOT READY FOR
              PRODUCTION <Arrow diagonal />
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
              Give your agent language-agnostic compiler, analyzer and rule
              feedback after every meaningful edit. Check your project's rules,
              fix violations, and verify the affected code with{" "}
              <strong>evidence you can inspect.</strong>
            </p>
            <div className="hero-buttons">
              <a className="button" href="#get-started">
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
        <div className="languages-bar container">
          <p>
            One interface.
            <br />
            <strong>Your languages. Your rules.</strong>
          </p>
          <div className="language-item">
            <span className="language-symbol">RS</span>
            <div>
              <strong>Rust</strong>
              <span>Native executable</span>
            </div>
          </div>
          <div className="language-item">
            <span className="language-symbol">LSP</span>
            <div>
              <strong>Language services</strong>
              <span>Configured for your files</span>
            </div>
          </div>
          <div className="language-item">
            <span className="language-symbol">+</span>
            <div>
              <strong>Rule enforcers</strong>
              <span>Your project policies</span>
            </div>
          </div>
          <div className="language-item">
            <span className="language-symbol">MCP</span>
            <div>
              <strong>Coding agents</strong>
              <span>The same feedback tools</span>
            </div>
          </div>
        </div>
        <section
          className="features container section-space"
          aria-labelledby="features-title"
        >
          <div className="section-intro">
            <p className="eyebrow">A SHORTER EDIT-CHECK-FIX LOOP.</p>
            <h2 id="features-title">
              Catch violations early.
              <br />
              Verify every meaningful edit.
            </h2>
            <p>
              Keep feedback close to the edit. Persistent local sessions reuse
              loaded project context, while your existing configuration defines
              the rules your agent checks.
            </p>
          </div>
          <div className="feature-grid">
            <article>
              <span className="feature-number">[01]</span>
              <h3>Get feedback while you work.</h3>
              <p>
                Check edits without rebuilding language-server context for every
                call. Get precise compiler, type, analyzer and lint findings for
                the files that matter.
              </p>
              <code>edit / check / fix</code>
            </article>
            <article>
              <span className="feature-number">[02]</span>
              <h3>Enforce your project rules.</h3>
              <p>
                Apply the checks your project defines through compiler, analyzer
                and rule-server configuration. Agents use the reported
                violations to correct their changes.
              </p>
              <code>LSP / native configuration / custom rules</code>
            </article>
            <article>
              <span className="feature-number">[03]</span>
              <h3>Verify the feedback.</h3>
              <p>
                See what was checked and whether the analysis completed on
                current inputs. Inspect findings separately: completed analysis
                can still report rule violations.
              </p>
              <code>complete · freshness · scope</code>
            </article>
          </div>
        </section>
        <section
          className="custom-rules container"
          aria-labelledby="custom-rules-title"
        >
          <p className="eyebrow">YOUR RULES. THE SAME FEEDBACK LOOP.</p>
          <h2 id="custom-rules-title">
            Sharper speaks LSP. Bring your own enforcer.
          </h2>
          <p>
            Built a custom rule server? If it implements the supported Language
            Server Protocol contract, connect it to your agent through Sharper.
            Configure it once; matching files include its findings alongside
            native project checks.
          </p>
          <a className="text-link" href={docs("custom-language-servers")}>
            Configure a custom LSP server <Arrow diagonal />
          </a>
        </section>
        <section
          className="navigation-perk container"
          aria-labelledby="navigation-title"
        >
          <h2 id="navigation-title">
            Need more context? Navigate the code, too.
          </h2>
          <p>
            Definitions, implementations and references help your agent
            understand the impact of a change. These supporting tools share the
            same local MCP interface.
          </p>
          <a className="text-link" href={docs("code-navigation")}>
            Explore code navigation <Arrow diagonal />
          </a>
        </section>
        <section
          id="workflow"
          className="workflow section-space"
          aria-labelledby="workflow-title"
        >
          <div className="container workflow-grid">
            <div>
              <p className="eyebrow">THE EVERYDAY EDIT LOOP</p>
              <h2 id="workflow-title">
                Edit. Check.
                <br />
                Fix. Verify.
              </h2>
              <p className="section-lead">
                Turn project rules into actionable feedback.
                <br />
                Close the loop after every edit.
              </p>
              <ol className="workflow-list">
                <li>
                  <span>01</span>
                  <div>
                    <h3>Establish the baseline</h3>
                    <p>
                      Check the relevant files before editing so your agent can
                      distinguish existing findings from new violations.
                    </p>
                  </div>
                </li>
                <li>
                  <span>02</span>
                  <div>
                    <h3>Edit and check</h3>
                    <p>
                      Make the edit with your normal tools, then request
                      feedback for the affected files and dependent code.
                    </p>
                  </div>
                </li>
                <li>
                  <span>03</span>
                  <div>
                    <h3>Fix violations and verify</h3>
                    <p>
                      Correct introduced violations and recheck. Confirm
                      current, completed analysis, inspect remaining findings,
                      and run relevant builds and tests separately.
                    </p>
                  </div>
                </li>
              </ol>
            </div>
            <div className="setup-example">
              <div className="setup-window">
                <div className="setup-window-top">
                  <span className="terminal-icon" aria-hidden="true">
                    ›_
                  </span>
                  <span>guided setup</span>
                  <span>EXAMPLE</span>
                </div>
                <div className="terminal-body">
                  <p className="terminal-command">
                    <span>$</span> sharper setup
                  </p>
                  <p className="terminal-brand">SHARPER</p>
                  <p>
                    Let's connect your languages
                    <br />
                    and coding agents.
                  </p>
                  <p className="terminal-step">
                    1 / 3&nbsp; Choose your languages
                  </p>
                  <p>
                    <span className="terminal-check">✓</span> Your configured
                    languages
                  </p>
                  <p className="terminal-step">
                    2 / 3&nbsp; Connect your LSP servers
                  </p>
                  <p>
                    <span className="terminal-check">✓</span> Existing servers
                    detected
                    <br />
                    <span className="terminal-muted">
                      &nbsp; Install servers with their own tooling.
                    </span>
                  </p>
                  <p className="terminal-step">
                    3 / 3&nbsp; Connect your agents
                  </p>
                  <p>
                    <span className="terminal-check">✓</span> Choose a client
                    and confirm registration
                  </p>
                  <p className="terminal-end">
                    Ready for your next edit.
                    <span className="terminal-cursor" />
                  </p>
                </div>
              </div>
              <p className="setup-caption">
                Works with <strong>Codex</strong>, <strong>Claude Code</strong>,{" "}
                <strong>OpenCode</strong> and <strong>GitHub Copilot</strong>.
              </p>
            </div>
          </div>
        </section>
        <section
          id="get-started"
          className="container section-space start-section"
          aria-labelledby="start-title"
        >
          <div className="start-copy">
            <p className="eyebrow">MAKE YOUR NEXT EDIT SHARPER</p>
            <h2 id="start-title">
              Start local.
              <br />
              Stay in control.
            </h2>
            <p>
              Build the current source candidate, then let guided setup connect
              your languages and agents.
            </p>
            <p className="requirements">
              Requires the Rust toolchain in{" "}
              <a href={`${repository}/blob/main/rust-toolchain.toml`}>
                rust-toolchain.toml
              </a>{" "}
              and your platform's native linker. Restore target-project
              dependencies before analysis.
            </p>
            <a className="text-link" href={docs("native-installation")}>
              Full installation guide <Arrow diagonal />
            </a>
          </div>
          <div className="install-panel">
            <div className="install-heading">
              <div
                className="platform-switch"
                role="group"
                aria-label="Build platform"
              >
                <button
                  type="button"
                  aria-pressed={platform === "windows"}
                  onClick={() => setPlatform("windows")}
                >
                  Windows
                </button>
                <button
                  type="button"
                  aria-pressed={platform === "linux"}
                  onClick={() => setPlatform("linux")}
                >
                  Linux
                </button>
              </div>
              <span>SOURCE · {process.env.NEXT_PUBLIC_SHARPER_VERSION}</span>
            </div>
            <div className="install-code">
              <CopyButton value={install} />
              <pre tabIndex={0} aria-label="Install commands">
                <code>{install}</code>
              </pre>
            </div>
            <p className="install-note">
              Native Rust executable · Windows x64 / Linux x64 (glibc)
              <br />
              <a href={`${repository}/releases`}>Check published releases</a> or
              read about <a href={docs("npm")}>npm distribution</a>. Registry
              availability is separate from this source build.
            </p>
          </div>
        </section>
        <section
          id="documentation"
          className="documentation container section-space"
          aria-labelledby="docs-title"
        >
          <div className="docs-heading">
            <div>
              <p className="eyebrow">GO A LITTLE DEEPER</p>
              <h2 id="docs-title">The details are open.</h2>
            </div>
            <p>
              Practical guides and explicit contracts.
              <br />
              Documentation lives with the code on GitHub.
            </p>
          </div>
          <div className="docs-grid">
            {documents.map((document) => (
              <a
                className="doc-card"
                key={document.file}
                href={docs(document.file)}
              >
                <div className="doc-top">
                  <span>{document.tag}</span>
                  <Arrow diagonal />
                </div>
                <h3>{document.title}</h3>
                <p>{document.description}</p>
                <span className="doc-number">/{document.number}</span>
              </a>
            ))}
          </div>
        </section>
        <section className="project-note container">
          <img src={asset(`icon-${theme}.png`)} width="64" height="64" alt="" />
          <div>
            <h2>Built in the open. Still taking shape.</h2>
            <p>
              Sharper is pre-alpha and intended for evaluation and development.
              Interfaces may change, and production qualification is incomplete.
            </p>
          </div>
          <a className="text-link" href={docs("production-readiness")}>
            See release requirements <Arrow diagonal />
          </a>
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
