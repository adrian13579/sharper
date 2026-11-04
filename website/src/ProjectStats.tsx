"use client";
import { useEffect, useState } from "react";
const format = new Intl.NumberFormat("en", {
  notation: "compact",
  maximumFractionDigits: 1,
});
export default function ProjectStats() {
  const [stars, setStars] = useState<number | null>(null);
  const [downloads, setDownloads] = useState<number | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    const timer = setTimeout(() => controller.abort(), 5000);
    async function load(
      url: string,
      field: string,
      set: (value: number) => void,
    ) {
      try {
        const response = await fetch(url, { signal: controller.signal });
        if (!response.ok) return;
        const data = await response.json();
        const value = data[field];
        if (active && Number.isSafeInteger(value) && value >= 0) set(value);
      } catch {}
    }
    void Promise.all([
      load(
        "https://api.github.com/repos/adrian13579/sharper",
        "stargazers_count",
        setStars,
      ),
      load(
        "https://api.npmjs.org/downloads/point/last-month/@adrian13579/sharper",
        "downloads",
        setDownloads,
      ),
    ]).finally(() => clearTimeout(timer));
    return () => {
      active = false;
      controller.abort();
      clearTimeout(timer);
    };
  }, []);
  return (
    <div className="project-stats" aria-label="Project statistics">
      <a
        href="https://github.com/adrian13579/sharper/stargazers"
        aria-label="GitHub stars"
        title={
          stars === null
            ? "GitHub count currently unavailable"
            : `${stars.toLocaleString("en")} GitHub stars`
        }
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          aria-hidden="true"
        >
          <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z" />
        </svg>
        <strong>{stars === null ? "—" : format.format(stars)}</strong>
        <span>GitHub stars</span>
      </a>
      <span className="stats-divider" aria-hidden="true" />
      <a
        href="https://www.npmjs.com/package/@adrian13579/sharper"
        aria-label="npm downloads per month"
        title={
          downloads === null
            ? "npm count currently unavailable"
            : `${downloads.toLocaleString("en")} downloads in the last month`
        }
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          aria-hidden="true"
        >
          <path d="M12 3v12m-4-4 4 4 4-4M4 17v4h16v-4" />
        </svg>
        <strong>{downloads === null ? "—" : format.format(downloads)}</strong>
        <span>downloads / month</span>
      </a>
    </div>
  );
}
