const packageName = "@adrian13579/sharper";
const day = 86400000;
function date(value: number) {
  return new Date(value).toISOString().slice(0, 10);
}
export async function totalDownloads(signal: AbortSignal): Promise<number> {
  async function get(url: string) {
    const response = await fetch(url, { signal });
    if (!response.ok) throw new Error("npm statistics unavailable");
    return response.json();
  }
  const [metadata, latest] = await Promise.all([
    get(`https://registry.npmjs.org/${packageName}`),
    get(`https://api.npmjs.org/downloads/point/last-day/${packageName}`),
  ]);
  const start = Date.parse(metadata.time?.created?.slice(0, 10));
  const end = Date.parse(latest.end);
  if (
    !Number.isFinite(start) ||
    !Number.isFinite(end) ||
    start < Date.parse("2015-01-10") ||
    start > end
  )
    throw new Error("Incomplete npm history");
  const periods: Array<{ start: string; end: string }> = [];
  // npm limits individual queries to 18 months; use non-overlapping annual chunks.
  // https://github.com/npm/registry/blob/main/docs/download-counts.md#limits
  for (let from = start; from <= end; from += 365 * day)
    periods.push({
      start: date(from),
      end: date(Math.min(end, from + 364 * day)),
    });
  const counts = await Promise.all(
    periods.map(async (period) => {
      const result = await get(
        `https://api.npmjs.org/downloads/point/${period.start}:${period.end}/${packageName}`,
      );
      if (
        result.start !== period.start ||
        result.end !== period.end ||
        !Number.isSafeInteger(result.downloads) ||
        result.downloads < 0
      )
        throw new Error("Incomplete npm statistics");
      return result.downloads as number;
    }),
  );
  const total = counts.reduce((sum, count) => sum + count, 0);
  if (!Number.isSafeInteger(total)) throw new Error("Invalid npm total");
  return total;
}
