import { notFound } from "next/navigation";
import { getDocuments, entries } from "../../../lib/docs";
import Docs from "../../../Docs";
export const dynamicParams = false;
export function generateStaticParams() {
  return entries.map(({ slug }) => ({ slug }));
}
export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const entry = entries.find((page) => page.slug === slug);
  return {
    title: `${entry?.title || "Documentation"} | Sharper`,
    alternates: {
      canonical: `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/docs/${slug}/`,
    },
  };
}
export default async function Page({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const documents = getDocuments();
  if (!documents.some((doc) => doc.slug === slug)) notFound();
  return <Docs documents={documents} slug={slug} />;
}
