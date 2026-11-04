import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const screenshots = path.resolve("../artifacts/website");

// Keep layout checks independent of the external badge provider's availability.
test.beforeEach(async ({ page }) => {
  await page.route("https://img.shields.io/**", (route) =>
    route.fulfill({
      contentType: "image/svg+xml",
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="140" height="20"><rect width="140" height="20" fill="#555"/></svg>',
    }),
  );
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
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Sharper code.",
  );
  await expect(
    page.getByText("RAPID RULE FEEDBACK", { exact: false }),
  ).toBeVisible();
  await expect(page.locator("main > section")).toHaveCount(1);
  const stats = page.getByLabel("Project statistics");
  await expect(
    stats.getByRole("link", { name: "GitHub stars", exact: true }),
  ).toHaveAttribute("href", /github.com\/adrian13579\/sharper\/stargazers$/);
  await expect(
    stats.getByRole("img", { name: "npm downloads per month" }),
  ).toHaveAttribute("src", /shields.io\/npm\/dm\/@adrian13579\/sharper/);

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

test("feedback leads the page and the hero has no console example", async ({
  page,
}) => {
  await page.goto("./");
  await expect(page.locator(".hero")).toContainText("Fast feedback.");
  await expect(
    page.locator(".hero pre, .hero .demo-shell, .hero .setup-window"),
  ).toHaveCount(0);
  await expect(
    page.getByRole("link", { name: "Start building" }),
  ).toHaveAttribute("href", "/sharper/docs/npm/");
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
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(
    "Rapid rule feedback for your agent",
  );
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
