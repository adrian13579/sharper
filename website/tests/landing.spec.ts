import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const screenshots = path.resolve("../artifacts/website");

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
  const cards = page.locator(".doc-card");
  await expect(cards).toHaveCount(6);
  for (const link of await cards.all())
    await expect(link).toHaveAttribute("href", /^\/sharper\/docs\/.+\/$/);
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
    page.getByRole("link", { name: /Configure a custom LSP server/ }),
  ).toHaveAttribute("href", /docs\/custom-language-servers\/$/);
  await expect(
    page.locator(".hero pre, .hero .demo-shell, .hero .setup-window"),
  ).toHaveCount(0);
  await expect(
    page.getByRole("heading", { name: "Enforce your project rules." }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: /Feedback & rule verification/ }),
  ).toHaveAttribute("href", /docs\/feedback\/$/);
  await expect(
    page.getByRole("link", { name: /Explore code navigation/ }),
  ).toBeVisible();
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

test("installation method changes copyable commands", async ({
  page,
  context,
}) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto("./");
  await page.getByRole("button", { name: "npx", exact: true }).click();
  await expect(
    page.getByLabel("Install commands", { exact: true }),
  ).toContainText("npx --yes @adrian13579/sharper@next");
  await page.getByRole("button", { name: "Copy install commands" }).click();
  await expect(page.getByRole("status")).toContainText("Commands copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(
    "npx --yes @adrian13579/sharper@next",
  );
  await page.getByRole("button", { name: "npm", exact: true }).click();
  await expect(
    page.getByLabel("Install commands", { exact: true }),
  ).toContainText("npm install --global @adrian13579/sharper@next");
  expect(
    await page.getByLabel("Install commands", { exact: true }).textContent(),
  ).not.toContain("cargo install");
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
    expect(
      await page
        .locator(".doc-card > p")
        .first()
        .evaluate((element) => parseFloat(getComputedStyle(element).fontSize)),
    ).toBeGreaterThanOrEqual(16);
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
