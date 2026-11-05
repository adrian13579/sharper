import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdir, readFile } from "node:fs/promises";
test("docs navigation, full text search, anchors and every page work", async ({
  page,
  request,
}) => {
  await page.goto("./docs/overview/");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  const nav = page.getByRole("navigation", { name: "Documentation sections" });
  const links = await nav
    .getByRole("link")
    .evaluateAll((nodes) => nodes.map((node) => node.getAttribute("href")!));
  expect(links.length).toBeGreaterThan(0);
  expect(links.slice(0, 3)).toEqual([
    "/sharper/docs/overview/",
    "/sharper/docs/npm/",
    "/sharper/docs/custom-language-servers/",
  ]);
  expect(links.indexOf("/sharper/docs/responses/")).toBeLessThan(
    links.indexOf("/sharper/docs/architecture-rules/"),
  );
  for (const link of links) {
    const response = await request.get(link);
    expect(response.status()).toBe(200);
  }
  await page.getByRole("searchbox").fill("requireResultId");
  await expect(page.locator(".docs-search-results")).toContainText(
    "Server & agent setup",
  );
  await page
    .locator(".docs-search-results")
    .getByRole("link", { name: /Server & agent setup/ })
    .click();
  await expect(page).toHaveURL(/docs\/custom-language-servers\/$/);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page
    .getByRole("navigation", { name: "On this page" })
    .getByRole("link", { name: "Configure your services" })
    .click();
  await expect(page).toHaveURL(/#configure-your-services$/);
  const headings = await page.locator(".docs-article h2").allTextContents();
  expect(headings.indexOf("Connect your agent")).toBeGreaterThan(
    headings.indexOf("Configure your services"),
  );

  // Every local content link and heading fragment resolves.
  for (const link of links) {
    await page.goto(link);
    const contentLinks = await page
      .locator(".docs-article a")
      .evaluateAll((nodes) =>
        nodes
          .map((node) => node.getAttribute("href")!)
          .filter((href) => href.startsWith("/sharper/")),
      );
    for (const target of contentLinks) {
      const [url, fragment] = target.split("#");
      const response = await request.get(url);
      expect(response.status()).toBe(200);
      if (fragment)
        expect(await response.text()).toContain(
          `id="${decodeURIComponent(fragment)}"`,
        );
    }
  }
  await page.goto("./docs/overview/");
  await page.getByRole("searchbox").fill("no-such-rule-xyz");
  await expect(page.getByRole("status")).toHaveText("0 results");
  await page.getByRole("searchbox").fill("");
  for (let i = 0; i < links.length; i++) {
    await expect(
      page.locator(".docs-nav-group a[aria-current=page]"),
    ).toHaveAttribute("href", links[i]);
    const pagination = page.getByRole("navigation", {
      name: "Page navigation",
    });
    const previous = pagination.getByRole("link", { name: /^Previous / });
    if (i === 0) await expect(previous).toHaveCount(0);
    else await expect(previous).toHaveAttribute("href", links[i - 1]);
    const next = pagination.getByRole("link", { name: /^Next / });
    if (i < links.length - 1) {
      await expect(next).toHaveAttribute("href", links[i + 1]);
      await next.click();
      await expect(page).toHaveURL(new URL(links[i + 1], page.url()).href);
    } else await expect(next).toHaveCount(0);
  }
});
test("docs themes, mobile menu and accessibility", async ({ page }) => {
  await page.goto("./docs/overview/");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  for (const theme of ["light", "dark"]) {
    if ((await page.locator("html").getAttribute("data-theme")) !== theme)
      await page
        .getByRole("button", { name: `Switch to ${theme} theme` })
        .click();
    const results = await new AxeBuilder({ page }).analyze();
    expect(
      results.violations.filter(
        (v) => v.impact === "serious" || v.impact === "critical",
      ),
    ).toEqual([]);
  }
  await mkdir("../artifacts/website", { recursive: true });
  await page.screenshot({
    path: "../artifacts/website/docs-desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({
    path: "../artifacts/website/docs-overview-mobile.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "Sections", exact: true }).click();
  await expect(page.getByRole("searchbox")).toBeVisible();
  await page
    .getByRole("navigation", { name: "Documentation sections" })
    .getByRole("link", { name: "Installation", exact: true })
    .click();
  await expect(page).toHaveURL(/docs\/npm\/$/);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: "../artifacts/website/docs-mobile.png",
    fullPage: true,
  });
});

test("JSON examples are highlighted without changing their contents", async ({
  page,
}) => {
  const markdown = (
    await readFile("content/docs/responses.md", "utf8")
  ).replace(/\r\n?/g, "\n");
  const source = Array.from(
    markdown.matchAll(/```json\s*\n([\s\S]*?)```/g),
    (match) => match[1],
  );
  await page.goto("./docs/responses/");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  const blocks = page.locator("pre code.language-json");
  await expect(blocks).toHaveCount(source.length);
  for (let i = 0; i < source.length; i++)
    expect(await blocks.nth(i).textContent()).toBe(source[i]);
  expect(JSON.parse((await blocks.first().textContent())!)).toMatchObject({
    status: "completed",
    diagnosticCount: 1,
  });
  for (const theme of ["light", "dark"]) {
    if (theme === "dark")
      await page.getByRole("button", { name: "Switch to dark theme" }).click();
    const key = await blocks
      .first()
      .locator(".json-key")
      .first()
      .evaluate((node) => getComputedStyle(node).color);
    const value = await blocks
      .first()
      .locator(".json-string")
      .first()
      .evaluate((node) => getComputedStyle(node).color);
    expect(key).not.toBe(value);
    await mkdir("../artifacts/website", { recursive: true });
    await blocks.first().scrollIntoViewIfNeeded();
    await page.screenshot({ path: `../artifacts/website/json-${theme}.png` });
    const results = await new AxeBuilder({ page })
      .include(".docs-main")
      .analyze();
    expect(
      results.violations.filter(
        (v) => v.impact === "serious" || v.impact === "critical",
      ),
    ).toEqual([]);
  }
});
