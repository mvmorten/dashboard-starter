// Tests for the list's URL state. Run with `npm test` (Node's own test runner).

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import {
  apiQuery,
  isDefaultView,
  readListState,
  sortBy,
  withFilter,
  withPage,
  withSearch,
  withSort,
  writeListState,
  type ListSpec,
} from "./listState.ts";

const spec: ListSpec = {
  sorts: [
    { key: "name", firstDir: "asc" },
    { key: "email", firstDir: "asc" },
    { key: "updated", firstDir: "desc" },
  ],
  defaultSort: "name",
  filters: { status: ["Active", "Inactive"] },
  paged: true,
  searchable: true,
};

const read = (query: string) => readListState(new URLSearchParams(query), spec);
const url = (query: string) => writeListState(read(query), spec).toString();

describe("readListState", () => {
  test("an empty URL is the default view", () => {
    assert.deepEqual(read(""), {
      search: "",
      filters: { status: "" },
      sort: "name",
      dir: "asc",
      page: 1,
    });
  });

  test("reads every value it knows", () => {
    assert.deepEqual(
      read("search=ab&status=Inactive&sort=updated&dir=asc&page=3"),
      {
        search: "ab",
        filters: { status: "Inactive" },
        sort: "updated",
        dir: "asc",
        page: 3,
      },
    );
  });

  test("a sort without a dir takes that column's first direction", () => {
    assert.equal(read("sort=updated").dir, "desc");
    assert.equal(read("sort=email").dir, "asc");
  });

  test("values it doesn't know are the defaults, never passed on", () => {
    const state = read("sort=Name&dir=up&status=active&page=-2");
    assert.equal(state.sort, "name");
    assert.equal(state.dir, "asc");
    assert.equal(state.filters.status, "");
    assert.equal(state.page, 1);
    assert.equal(read("page=2.5").page, 1);
    assert.equal(read("page=abc").page, 1);
  });

  test("the search is trimmed", () => {
    assert.equal(read("search=%20%20ab%20").search, "ab");
  });

  test("a list without search or paging ignores them", () => {
    const members: ListSpec = {
      ...spec,
      paged: false,
      searchable: false,
      filters: {},
    };
    const state = readListState(
      new URLSearchParams("search=x&page=4"),
      members,
    );
    assert.equal(state.search, "");
    assert.equal(state.page, 1);
  });
});

describe("writeListState", () => {
  test("the default view is an empty query string", () => {
    assert.equal(url(""), "");
    assert.equal(url("sort=name&dir=asc&page=1&status="), "");
  });

  test("sort and dir are written together, or not at all", () => {
    assert.equal(url("sort=updated"), "sort=updated&dir=desc");
    assert.equal(url("dir=desc"), "sort=name&dir=desc");
  });

  test("a read and a write give the same URL back", () => {
    const query = "search=ab&status=Active&sort=email&dir=desc&page=2";
    assert.equal(url(query), query);
  });
});

describe("apiQuery", () => {
  test("always spells out the sort and the page", () => {
    assert.equal(
      apiQuery(read(""), spec).toString(),
      "sort=name&dir=asc&page=1",
    );
  });
});

describe("changing the view", () => {
  const onPage3 = read("search=ab&sort=email&page=3");

  test("a header click on another column sorts it in its first direction, from page 1", () => {
    assert.deepEqual(
      { ...sortBy(onPage3, "updated", spec) },
      { ...onPage3, sort: "updated", dir: "desc", page: 1 },
    );
  });

  test("a second click on the same column reverses it", () => {
    const once = sortBy(read(""), "email", spec);
    assert.equal(once.dir, "asc");
    assert.equal(sortBy(once, "email", spec).dir, "desc");
    assert.equal(sortBy(sortBy(once, "email", spec), "email", spec).dir, "asc");
  });

  test("a search, filter or the phone's sort select go back to page 1", () => {
    assert.equal(withSearch(onPage3, " cd ").page, 1);
    assert.equal(withSearch(onPage3, " cd ").search, "cd");
    assert.equal(withFilter(onPage3, "status", "Active").page, 1);
    assert.equal(withSort(onPage3, "name", "desc").page, 1);
  });

  test("paging keeps everything else", () => {
    assert.deepEqual(withPage(onPage3, 4), { ...onPage3, page: 4 });
  });
});

describe("isDefaultView", () => {
  test("only when search, filters and sort are all the default", () => {
    assert.equal(isDefaultView(read(""), spec), true);
    assert.equal(isDefaultView(read("page=4"), spec), true);
    assert.equal(isDefaultView(read("search=a"), spec), false);
    assert.equal(isDefaultView(read("status=Active"), spec), false);
    assert.equal(isDefaultView(read("sort=email"), spec), false);
    assert.equal(isDefaultView(read("dir=desc"), spec), false);
  });
});
