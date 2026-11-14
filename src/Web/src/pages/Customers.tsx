import { useState, type FormEvent } from "react";
import { Link, useLocation, useSearchParams } from "react-router";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { currentTenant } from "../auth/me";
import { useMe } from "../auth/useMe";
import type { CustomerListItem } from "../customers/customers";
import { useCustomers } from "../customers/useCustomers";
import { useMediaQuery } from "../layout/useMediaQuery";
import { usePageTitle } from "../layout/usePageTitle";
import NoAccess from "./NoAccess";

// What the form tells the list after it navigates here (a delete), in history state.
export interface CustomersNotice {
  notice: string;
}

// The current tenant's customers: a search box, 25 a page, a table on a wide screen and cards
// on a phone. The search and page live in the URL (?search=&page=), so Back, a reload and a
// shared link all show the same page.
export default function Customers() {
  usePageTitle("Customers");
  const { data: me } = useMe();
  const [params, setParams] = useSearchParams();
  const search = params.get("search") ?? "";
  const page = pageFrom(params.get("page"));
  const customers = useCustomers(search, page);
  const wide = useMediaQuery("(min-width: 48rem)");
  const notice = (useLocation().state as CustomersNotice | null)?.notice;

  if (!can(me, Permissions.customersView)) {
    return <NoAccess />;
  }

  if (customers.isError) {
    return customers.error instanceof ApiError &&
      customers.error.status === 403 ? (
      <NoAccess />
    ) : (
      <div role="alert">
        <h1>Customers</h1>
        <p className="lead">The server didn&apos;t answer.</p>
        <button type="button" onClick={() => void customers.refetch()}>
          Try again
        </button>
      </div>
    );
  }

  function goTo(nextSearch: string, nextPage: number) {
    const next = new URLSearchParams();
    if (nextSearch) {
      next.set("search", nextSearch);
    }

    if (nextPage > 1) {
      next.set("page", String(nextPage));
    }

    setParams(next);
  }

  const tenant = me ? currentTenant(me) : null;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Customers</h1>
          <p className="lead">
            The customers of {tenant?.name ?? "this tenant"}.
          </p>
        </div>
        {can(me, Permissions.customersEdit) && (
          <Link to="/customers/new" className="button-link primary-link">
            New customer
          </Link>
        )}
      </div>

      <div role="status">{notice && <p className="notice">{notice}</p>}</div>

      {/* The key starts the box over when the URL's search changes (Back, a link). */}
      <SearchForm
        key={search}
        search={search}
        onSearch={(value) => goTo(value, 1)}
      />

      {customers.isPending ? (
        <p role="status">Loading...</p>
      ) : (
        <>
          <ResultSummary
            search={search}
            total={customers.data.total}
            page={customers.data.page}
            pageSize={customers.data.pageSize}
            shown={customers.data.items.length}
          />
          {customers.data.items.length > 0 &&
            (wide ? (
              <CustomerTable customers={customers.data.items} />
            ) : (
              <CustomerCards customers={customers.data.items} />
            ))}
          <Pager
            page={customers.data.page}
            pageSize={customers.data.pageSize}
            total={customers.data.total}
            loading={customers.isPlaceholderData}
            onPage={(next) => {
              goTo(search, next);
              // The pager is at the bottom; the new page starts at the top.
              window.scrollTo({ top: 0 });
            }}
          />
        </>
      )}
    </>
  );
}

function pageFrom(value: string | null): number {
  const page = Number(value);
  return Number.isInteger(page) && page >= 1 ? page : 1;
}

// A plain form: type, then press Enter or Search. No search-as-you-type, so there's no
// request on every key and no timer to explain.
function SearchForm({
  search,
  onSearch,
}: {
  search: string;
  onSearch: (value: string) => void;
}) {
  const [value, setValue] = useState(search);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSearch(value.trim());
  }

  return (
    <form role="search" className="search-form" onSubmit={submit}>
      <label htmlFor="customer-search" className="visually-hidden">
        Search customers
      </label>
      <input
        id="customer-search"
        type="search"
        placeholder="Name or email starts with..."
        autoComplete="off"
        value={value}
        onChange={(event) => setValue(event.target.value)}
      />
      <button type="submit">Search</button>
      {search && (
        <button type="button" onClick={() => onSearch("")}>
          Clear
        </button>
      )}
    </form>
  );
}

function ResultSummary({
  search,
  total,
  page,
  pageSize,
  shown,
}: {
  search: string;
  total: number;
  page: number;
  pageSize: number;
  shown: number;
}) {
  if (total === 0) {
    return (
      <p className="result-summary">
        {search
          ? `No customers have a name or email starting with "${search}".`
          : "No customers yet."}
      </p>
    );
  }

  if (shown === 0) {
    return (
      <p className="result-summary">
        Page {page} is past the end: there are {total} customers.
      </p>
    );
  }

  const first = (page - 1) * pageSize + 1;
  const last = first + shown - 1;
  return (
    <p className="result-summary">
      {first === 1 && last === total
        ? `${total} ${total === 1 ? "customer" : "customers"}`
        : `${first} to ${last} of ${total}`}
      {search && ` starting with "${search}"`}
    </p>
  );
}

function CustomerTable({ customers }: { customers: CustomerListItem[] }) {
  return (
    <div className="table-scroll">
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Email</th>
            <th scope="col">Phone</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {customers.map((customer) => (
            <tr key={customer.id}>
              <th scope="row">
                <Link to={`/customers/${customer.id}`}>{customer.name}</Link>
              </th>
              <td className="break-anywhere">{customer.email}</td>
              <td className="nowrap">{customer.phone}</td>
              <td>
                <StatusBadge status={customer.status} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// On a phone, the whole card is the link: a big target, and no table to scroll sideways.
function CustomerCards({ customers }: { customers: CustomerListItem[] }) {
  return (
    <ul className="card-list">
      {customers.map((customer) => (
        <li key={customer.id}>
          <Link to={`/customers/${customer.id}`} className="card-list-link">
            <span className="card-list-head">
              <span className="card-list-title">{customer.name}</span>
              <StatusBadge status={customer.status} />
            </span>
            {customer.email && (
              <span className="card-list-line break-anywhere">
                {customer.email}
              </span>
            )}
            {customer.phone && (
              <span className="card-list-line">{customer.phone}</span>
            )}
          </Link>
        </li>
      ))}
    </ul>
  );
}

function StatusBadge({ status }: { status: CustomerListItem["status"] }) {
  return (
    <span
      className={
        status === "Active" ? "status-badge" : "status-badge status-badge-off"
      }
    >
      {status}
    </span>
  );
}

function Pager({
  page,
  pageSize,
  total,
  loading,
  onPage,
}: {
  page: number;
  pageSize: number;
  total: number;
  loading: boolean;
  onPage: (page: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages === 1 && page === 1) {
    return null;
  }

  return (
    <nav className="pager" aria-label="Pages">
      <button
        type="button"
        disabled={page <= 1 || loading}
        onClick={() => onPage(Math.min(page - 1, pages))}
      >
        Previous
      </button>
      <span className="pager-position">
        Page {page} of {pages}
      </span>
      <button
        type="button"
        disabled={page >= pages || loading}
        onClick={() => onPage(page + 1)}
      >
        Next
      </button>
    </nav>
  );
}
