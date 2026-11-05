"use client";
import { useLayoutEffect, useState } from "react";
export type Theme = "light" | "dark";
function apply(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme;
}
export function useTheme() {
  const [theme, setTheme] = useState<Theme>("light");
  useLayoutEffect(() => {
    let next: Theme = window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
    try {
      const saved = localStorage.getItem("sharper-theme");
      if (saved === "light" || saved === "dark") next = saved;
    } catch {}
    apply(next);
    setTheme(next);
  }, []);
  function toggleTheme() {
    const next = theme === "light" ? "dark" : "light";
    apply(next);
    setTheme(next);
    try {
      localStorage.setItem("sharper-theme", next);
    } catch {}
  }
  return { theme, toggleTheme };
}
