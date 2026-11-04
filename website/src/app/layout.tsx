import type { Metadata } from "next";
import "../styles.css";
const base = process.env.NEXT_PUBLIC_BASE_PATH || "";
export const metadata: Metadata = {
  title: "Sharper - a sharper edge for your coding agent",
  description:
    "Language-agnostic feedback and rule verification for coding agents. Connect your language services and custom rule enforcers through MCP.",
  metadataBase: new URL("https://adrian13579.github.io"),
  alternates: { canonical: `${base}/` },
  openGraph: {
    title: "Sharper - a sharper edge for your coding agent",
    description:
      "Fast feedback. Native project rules. Scoped verification for agent edits.",
    url: `${base}/`,
    type: "website",
  },
  icons: {
    icon: [
      {
        url: `${base}/brand/icon-light.png`,
        media: "(prefers-color-scheme: light)",
      },
      {
        url: `${base}/brand/icon-dark.png`,
        media: "(prefers-color-scheme: dark)",
      },
    ],
  },
};
export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
