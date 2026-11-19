// What a list shows, kept in the URL: the search, the filters, the sort and the page. A link or
// a reload gives the same view, and Back goes to the last one. Pure, with no imports, so
// `npm test` checks it without a browser (listState.test.ts).
//
// The query string has the API's names and values (?search=&status=&sort=&dir=&page=), so the
// URL in the address bar is the request the list makes. A value left out is the default, and
// a default is always left out: the plain list's URL is just its path.

export type SortDir = "asc" | "desc";

export interface SortSpec {
  key: string;
  // Which way the first click on this column sorts. Text: A to Z. Dates: newest first, which
  // is what someone sorting by "Updated" usually wants.
  firstDir: SortDir;
}

export interface ListSpec {
  // The columns that sort, as the API names them.
  sorts: readonly SortSpec[];
  // The sort with nothing in the URL, in its firstDir.
  defaultSort: string;
  // Each filter's allowed values. "" (left out of the URL) is "all".
  filters: Readonly<Record<string, readonly string[]>>;
  // False for a list that's all on one page (Members): no ?page= at all.
  paged: boolean;
  // False for a list without a search box.
  searchable: boolean;
}

export interface ListState {
  search: string;
  filters: Record<string, string>;
  sort: string;
  dir: SortDir;
  page: number;
}

export function defaultListState(spec: ListSpec): ListState {
  return {
    search: "",
    filters: Object.fromEntries(
      Object.keys(spec.filters).map((name) => [name, ""]),
    ),
    sort: spec.defaultSort,
    dir: firstDirOf(spec, spec.defaultSort),
    page: 1,
  };
}

// The URL's values, where the spec allows them. A value it doesn't know (an old link, a typo
// typed by hand) is read as the default here, so the page always asks the API for a view that
// exists. The API is stricter: it answers an unknown value with a 400.
export function readListState(
  params: URLSearchParams,
  spec: ListSpec,
): ListState {
  const state = defaultListState(spec);
  if (spec.searchable) {
    state.search = (params.get("search") ?? "").trim();
  }

  for (const [name, allowed] of Object.entries(spec.filters)) {
    const value = params.get(name) ?? "";
    if (allowed.includes(value)) {
      state.filters[name] = value;
    }
  }

  const sort = params.get("sort");
  if (sort !== null && spec.sorts.some((option) => option.key === sort)) {
    state.sort = sort;
    state.dir = firstDirOf(spec, sort);
  }

  const dir = params.get("dir");
  if (dir === "asc" || dir === "desc") {
    state.dir = dir;
  }

  if (spec.paged) {
    const page = Number(params.get("page"));
    state.page = Number.isInteger(page) && page >= 1 ? page : 1;
  }

  return state;
}

// The URL for a view, defaults left out. Sort and dir go together: either both are there, or
// neither (the default sort, its first direction).
export function writeListState(
  state: ListState,
  spec: ListSpec,
): URLSearchParams {
  const params = new URLSearchParams();
  if (spec.searchable && state.search) {
    params.set("search", state.search);
  }

  for (const name of Object.keys(spec.filters)) {
    const value = state.filters[name] ?? "";
    if (value) {
      params.set(name, value);
    }
  }

  if (!isDefaultSort(state, spec)) {
    params.set("sort", state.sort);
    params.set("dir", state.dir);
  }

  if (spec.paged && state.page > 1) {
    params.set("page", String(state.page));
  }

  return params;
}

// The query for the API: like the URL, but the sort is always spelled out, so the request
// doesn't lean on the API's own defaults.
export function apiQuery(state: ListState, spec: ListSpec): URLSearchParams {
  const params = writeListState(state, spec);
  params.set("sort", state.sort);
  params.set("dir", state.dir);
  if (spec.paged) {
    params.set("page", String(state.page));
  }

  return params;
}

// Anything to clear? The page number doesn't count: it's where you are, not what you chose.
export function isDefaultView(state: ListState, spec: ListSpec): boolean {
  return (
    state.search === "" &&
    Object.keys(spec.filters).every((name) => !state.filters[name]) &&
    isDefaultSort(state, spec)
  );
}

// A header click: the same column again reverses it; another column starts at its first
// direction. Either way, back to page 1, since the rows on page 3 are now different rows.
export function sortBy(
  state: ListState,
  key: string,
  spec: ListSpec,
): ListState {
  if (state.sort !== key) {
    return { ...state, sort: key, dir: firstDirOf(spec, key), page: 1 };
  }

  return { ...state, dir: state.dir === "asc" ? "desc" : "asc", page: 1 };
}

// The phone's sort select picks a column and a direction at once.
export function withSort(
  state: ListState,
  key: string,
  dir: SortDir,
): ListState {
  return { ...state, sort: key, dir, page: 1 };
}

// A new search or filter value: back to page 1, for the same reason.
export function withSearch(state: ListState, search: string): ListState {
  return { ...state, search: search.trim(), page: 1 };
}

export function withFilter(
  state: ListState,
  name: string,
  value: string,
): ListState {
  return { ...state, filters: { ...state.filters, [name]: value }, page: 1 };
}

export function withPage(state: ListState, page: number): ListState {
  return { ...state, page };
}

function isDefaultSort(state: ListState, spec: ListSpec): boolean {
  return (
    state.sort === spec.defaultSort &&
    state.dir === firstDirOf(spec, spec.defaultSort)
  );
}

export function firstDirOf(spec: ListSpec, key: string): SortDir {
  return spec.sorts.find((option) => option.key === key)?.firstDir ?? "asc";
}
