import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const screenshots = path.resolve("../artifacts/website");

// Deterministic provider responses for layout and accessibility checks.
test.beforeEach(async ({ page }) => {
  await page.route(
    "https://api.github.com/repos/adrian13579/sharper",
    (route) => route.fulfill({ json: { stargazers_count: 42 } }),
  );
  await page.route("https://registry.npmjs.org/@adrian13579/sharper", (route) =>
    route.fulfill({ json: { time: { created: "2026-01-01T00:00:00.000Z" } } }),
  );
  await page.route("https://api.npmjs.org/downloads/point/**", (route) => {
    const period = new URL(route.request().url()).pathname.split("/")[3];
    const [start, end] =
      period === "last-day" ? ["2026-11-03", "2026-11-03"] : period.split(":");
    return route.fulfill({ json: { downloads: 1234, start, end } });
  });
});

test("production base path, assets and documentation links load without errors", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("response", (response) => {
    if (
      response.status() >= 400 &&
      response.url().startsWith("http://127.0.0.1")
    )
      errors.push(response.url());
  });
  await page.goto("./");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  const stats = page.getByLabel("Project statistics");
  await expect(
    stats.getByRole("link", { name: "GitHub stars", exact: true }),
  ).toHaveAttribute("href", /github.com\/adrian13579\/sharper\/stargazers$/);
  await expect(stats).toContainText("42");
  await expect(stats).toContainText("1.2K");
  await expect(stats.locator("img")).toHaveCount(0);
  await expect(
    page
      .locator("header")
      .getByRole("link", { name: "Get started", exact: true }),
  ).toHaveCount(0);

  await expect(
    page.getByRole("link", { name: "Get started", exact: true }),
  ).toHaveAttribute("href", "/sharper/docs/npm/");
  expect(
    await page
      .locator("img")
      .evaluateAll((images) =>
        images.every(
          (image) =>
            image instanceof HTMLImageElement &&
            image.complete &&
            image.naturalWidth > 0,
        ),
      ),
  ).toBe(true);
  expect(errors).toEqual([]);
  await mkdir(screenshots, { recursive: true });
  await page.screenshot({
    path: path.join(screenshots, "desktop-light.png"),
    fullPage: true,
  });
});

test("theme persists and both desktop themes have no serious accessibility findings", async ({
  page,
}) => {
  await page.goto("./");
  for (const theme of ["light", "dark"] as const) {
    if (theme === "dark")
      await page.getByRole("button", { name: "Switch to dark theme" }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await expect(page.locator(".brand img")).toHaveAttribute(
      "src",
      new RegExp(`logo-${theme}\.png$`),
    );
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(results.violations).toEqual([]);
  }
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await mkdir(screenshots, { recursive: true });
  await page.screenshot({
    path: path.join(screenshots, "desktop-dark.png"),
    fullPage: true,
  });
});

test("mobile navigation, layout and accessibility remain usable", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("./");
  await page.getByRole("button", { name: "Open navigation" }).click();
  await expect(
    page.getByRole("button", { name: "Close navigation" }),
  ).toHaveAttribute("aria-expanded", "true");
  await page
    .getByRole("navigation", { name: "Main navigation" })
    .getByRole("link", { name: "Documentation", exact: true })
    .click();
  await expect(page).toHaveURL(/docs\/overview\/$/);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.goto("./");
  for (const width of [320, 390, 768, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    ).toBe(true);
  }
  await page.setViewportSize({ width: 390, height: 844 });
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
    .analyze();
  expect(results.violations).toEqual([]);
  await mkdir(screenshots, { recursive: true });
  await page.screenshot({
    path: path.join(screenshots, "mobile-light.png"),
    fullPage: true,
  });
});

test("statistics handle unavailable providers without fabricated counts", async ({
  page,
}) => {
  await page.route(
    "https://api.github.com/repos/adrian13579/sharper",
    (route) => route.fulfill({ status: 503, json: { message: "Unavailable" } }),
  );
  await page.route("https://api.npmjs.org/downloads/point/**", (route) =>
    route.fulfill({ status: 404, json: { error: "Not found" } }),
  );
  await page.goto("./");
  const stats = page.getByLabel("Project statistics");
  await expect(stats.locator("strong")).toHaveText(["—", "—"]);
  await expect(
    stats.getByRole("link", { name: "GitHub stars", exact: true }),
  ).toHaveAttribute("title", /unavailable/);
});

test("download total includes the complete history without overlapping periods", async ({
  page,
}) => {
  const periods: string[] = [];
  await page.route("https://registry.npmjs.org/@adrian13579/sharper", (route) =>
    route.fulfill({ json: { time: { created: "2024-01-01T00:00:00.000Z" } } }),
  );
  await page.route("https://api.npmjs.org/downloads/point/**", (route) => {
    const period = new URL(route.request().url()).pathname.split("/")[3];
    if (period === "last-day")
      return route.fulfill({ json: { end: "2026-11-03" } });
    periods.push(period);
    const [start, end] = period.split(":");
    return route.fulfill({ json: { downloads: 100, start, end } });
  });
  await page.goto("./");
  await expect(
    page.getByRole("link", { name: "Total npm downloads" }),
  ).toContainText("300");
  expect(periods.sort()).toEqual([
    "2024-01-01:2024-12-30",
    "2024-12-31:2025-12-30",
    "2025-12-31:2026-11-03",
  ]);
});
test("partial history is not displayed as a total", async ({ page }) => {
  await page.route("https://api.npmjs.org/downloads/point/**", (route) =>
    route.fulfill({
      json: { downloads: 50, start: "2026-09-01", end: "2026-11-03" },
    }),
  );
  await page.goto("./");
  await expect(
    page.getByRole("link", { name: "Total npm downloads" }),
  ).toContainText("—");
});
test("landing and documentation share the accent palette in both themes", async ({
  page,
}) => {
  await page.goto("./");
  for (const theme of ["light", "dark"]) {
    if (theme === "dark")
      await page.getByRole("button", { name: "Switch to dark theme" }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    const landing = await page
      .locator(".hero-rule-link")
      .evaluate((el) => getComputedStyle(el).color);
    const button = await page
      .locator(".hero .button")
      .evaluate((el) => getComputedStyle(el).backgroundColor);
    expect(button).toBe(landing);
    await page.goto("./docs/overview/");
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    expect(
      await page
        .locator(".docs-article a")
        .first()
        .evaluate((el) => getComputedStyle(el).color),
    ).toBe(landing);
    await page.goto("./");
  }
});
