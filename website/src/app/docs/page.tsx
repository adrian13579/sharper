import Docs from "../../Docs";
import { getDocuments } from "../../lib/docs";
export const metadata = {
  title: "Documentation | Sharper",
  alternates: { canonical: `${process.env.NEXT_PUBLIC_BASE_PATH || ""}/docs/` },
};
export default function Page() {
  return <Docs documents={getDocuments()} slug="overview" />;
}
