// A date and time from the API, in the reader's own time zone and language. The API sends UTC
// without a zone marker (SQL Server's datetime2), so add one before converting: without it,
// the browser would read the time as local.
export function formatUtc(value: string): string {
  return fromUtc(value).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

// The date alone, for a list's columns where the time would crowd out the names.
export function formatUtcDate(value: string): string {
  return fromUtc(value).toLocaleDateString(undefined, { dateStyle: "medium" });
}

// The same moment as an ISO string with its zone, for <time dateTime>.
export function utcIso(value: string): string {
  return fromUtc(value).toISOString();
}

function fromUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
}
