// A date and time from the API, in the reader's own time zone and language. The API sends UTC
// without a zone marker (SQL Server's datetime2), so add one before converting: without it,
// the browser would read the time as local.
export function formatUtc(value: string): string {
  const utc = /[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`;
  return new Date(utc).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}
