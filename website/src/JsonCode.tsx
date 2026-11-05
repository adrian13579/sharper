import type { ReactNode } from "react";

// Render tokens as text nodes so source whitespace and escaped characters stay intact.
export default function JsonCode({ text }: { text: string }) {
  const parts: ReactNode[] = [];
  const tokens =
    /"(?:[^"\\]|\\.)*"|\b(?:true|false|null)\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|[{}[\],:]/g;
  let offset = 0;
  for (const match of text.matchAll(tokens)) {
    const index = match.index;
    parts.push(text.slice(offset, index));
    const token = match[0];
    const kind = token.startsWith('"')
      ? /^\s*:/.test(text.slice(index + token.length))
        ? "key"
        : "string"
      : /^-?\d/.test(token)
        ? "number"
        : /^(true|false|null)$/.test(token)
          ? "literal"
          : "punctuation";
    parts.push(
      <span className={`json-${kind}`} key={index}>
        {token}
      </span>,
    );
    offset = index + token.length;
  }
  parts.push(text.slice(offset));
  return <>{parts}</>;
}
