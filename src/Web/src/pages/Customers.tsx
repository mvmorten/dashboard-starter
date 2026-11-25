import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { currentTenant } from "../auth/me";
import { useMe } from "../auth/useMe";
import {
  customerListSpec,
  customerListSpecWithDeleted,
  deletedFilter,
  type CustomerListItem,
} from "../customers/customers";
import { useCustomers } from "../customers/useCustomers";
import { formatUtcDate } from "../format";
import { visibleKeys } from "../grid/columns";
import { Email } from "../grid/Email";
import {
  ColumnPicker,
  DataTable,
  FilterSelect,
  SortSelect,
  type GridColumn,
} from "../grid/Grid";
import {
  defaultListState,
  firstDirOf,
  isDefaultView,
  sortBy,
  withFilter,
  withPage,
  withSearch,
  withSort,
  type ListState,
} from "../grid/listState";
import { Pager } from "../grid/Pager";
import { useColumnChoices } from "../grid/useColumnChoices";
import { useListState } from "../grid/useListState";
import { useArrivalNotice } from "../layout/useArrivalNotice";
import { useMediaQuery } from "../layout/useMediaQuery";
import { usePageTitle } from "../layout/usePageTitle";
import NoAccess from "./NoAccess";

// What the form tells the list after it navigates here (a delete), in history state. The
// tenant it happened in goes with it: switching tenants keeps the same history entry, and
// another tenant's list shouldn't say "Deleted: ..." about a customer it never had.
export interface CustomersNotice {
  notice: string;
  tenantId: number | null;
}

// The table's columns, in order. Name names the row, so it can't be hidden. Created starts
// hidden: Updated is the date people look for. Phone doesn't sort (the API has no such sort).
const columns: GridColumn<CustomerListItem>[] = [
  {
    key: "name",
    label: "Name",
    hideable: false,
    sortKey: "name",
    rowHeader: true,
    cell: (customer) => (
      <Link to={`/customers/${customer.id}`}>{customer.name}</Link>
    ),
  },
  {
    key: "email",
    label: "Email",
    hideable: true,
    sortKey: "email",
    cell: (customer) => customer.email && <Email address={customer.email} />,
  },
  {
    key: "phone",
    label: "Phone",
    hideable: true,
    className: "nowrap",
    cell: (customer) => customer.phone,
  },
  {
    key: "status",
    label: "Status",
    hideable: true,
    sortKey: "status",
    sortWords: { asc: "Active first", desc: "Inactive first" },
    cell: (customer) => <StatusBadge status={customer.status} />,
  },
  {
    key: "updated",
    label: "Updated",
    hideable: true,
    sortKey: "updated",
    sortWords: { asc: "oldest first", desc: "newest first" },
    className: "nowrap",
    cell: (customer) => formatUtcDate(customer.updatedAt),
  },
  {
    key: "created",
    label: "Created",
    hideable: true,
    hiddenByDefault: true,
    sortKey: "created",
    sortWords: { asc: "oldest first", desc: "newest first" },
    className: "nowrap",
    cell: (customer) => formatUtcDate(customer.createdAt),
  },
];

const statusOptions = [
  { value: "Active", label: "Active" },
  { value: "Inactive", label: "Inactive" },
];

// With customers.edit, the filter also finds the deleted customers, to restore them. It's in
// the same select because it's the same kind of choice: which customers the list shows.
const statusOptionsWithDeleted = [
  ...statusOptions,
  { value: deletedFilter, label: "Deleted" },
];

// The current tenant's customers: search, a status filter and a sort, 25 a page. A table on a
// wide screen (sortable headers, a choice of columns) and cards on a phone (a sort select).
// The whole view lives in the URL (?search=&status=&sort=&dir=&page=), so Back, a reload and
// a shared link all show the same page.
export default function Customers() {
  usePageTitle("Customers");
  const { data: me } = useMe();
  const canEdit = can(me, Permissions.customersEdit);
  const spec = canEdit ? customerListSpecWithDeleted : customerListSpec;
  const { state: view, show } = useListState(spec);
  const customers = useCustomers(view, spec);
  const { choices, toggle } = useColumnChoices("customers", columns);
  const wide = useMediaQuery("(min-width: 48rem)");
  // "Deleted: ..." from the form, shown once (layout/useArrivalNotice.ts).
  const [arrivedWith, dropNotice] = useArrivalNotice<CustomersNotice>();
  const notice =
    arrivedWith && arrivedWith.tenantId === me?.currentTenantId
      ? arrivedWith.notice
      : null;

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

  function go(next: ListState) {
    // A new view: the notice was about the last one.
    dropNotice();
    show(next);
  }

  const tenant = me ? currentTenant(me) : null;
  const visible = visibleKeys(columns, choices);
  const status = view.filters.status ?? "";

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Customers</h1>
          <p className="lead">
            The customers of {tenant?.name ?? "this tenant"}.
          </p>
        </div>
        {canEdit && (
          <Link to="/customers/new" className="button-link primary-link">
            New customer
          </Link>
        )}
      </div>

      <div role="status">{notice && <p className="notice">{notice}</p>}</div>

      <div className="grid-toolbar">
        {/* The key starts the box over when the URL's search changes (Back, a link, Clear). */}
        <SearchForm
          key={view.search}
          search={view.search}
          onSearch={(value) => go(withSearch(view, value))}
        />
        <div className="grid-controls">
          <FilterSelect
            label="Status"
            value={status}
            options={canEdit ? statusOptionsWithDeleted : statusOptions}
            onChange={(value) => go(withFilter(view, "status", value))}
          />
          {wide ? (
            <ColumnPicker
              columns={columns}
              visible={visible}
              onToggle={toggle}
            />
          ) : (
            <SortSelect
              columns={columns}
              sort={{ key: view.sort, dir: view.dir }}
              firstDirOf={(key) => firstDirOf(spec, key)}
              onSort={(key, dir) => go(withSort(view, key, dir))}
            />
          )}
          {!isDefaultView(view, spec) && (
            <button
              type="button"
              className="grid-clear"
              onClick={() => go(defaultListState(spec))}
            >
              Clear
            </button>
          )}
        </div>
      </div>

      {customers.isPending ? (
        <p role="status">Loading...</p>
      ) : (
        <>
          <ResultSummary
            search={view.search}
            status={status}
            total={customers.data.total}
            page={customers.data.page}
            pageSize={customers.data.pageSize}
            shown={customers.data.items.length}
          />
          {customers.data.items.length > 0 &&
            (wide ? (
              <DataTable
                columns={columns.filter((column) =>
                  visible.includes(column.key),
                )}
                rows={customers.data.items}
                rowKey={(customer) => customer.id}
                sort={{ key: view.sort, dir: view.dir }}
                onSort={(key) => go(sortBy(view, key, spec))}
              />
            ) : (
              <CustomerCards
                customers={customers.data.items}
                sort={view.sort}
              />
            ))}
          <Pager
            page={customers.data.page}
            pageSize={customers.data.pageSize}
            total={customers.data.total}
            loading={customers.isPlaceholderData}
            onPage={(next) => {
              go(withPage(view, next));
              // The pager is at the bottom; the new page starts at the top.
              window.scrollTo({ top: 0 });
            }}
          />
        </>
      )}
    </>
  );
}

// A plain form: type, then press Enter or Search. No search-as-you-type, so there's no
// request on every key and no timer to explain. Clearing the search is the toolbar's Clear.
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
    </form>
  );
}

function ResultSummary({
  search,
  status,
  total,
  page,
  pageSize,
  shown,
}: {
  search: string;
  status: string;
  total: number;
  page: number;
  pageSize: number;
  shown: number;
}) {
  // "customers", or "inactive customers" with the filter on.
  const kind = (count: number) =>
    `${status ? `${status.toLowerCase()} ` : ""}${count === 1 ? "customer" : "customers"}`;
  const starting = search ? ` starting with "${search}"` : "";

  if (total === 0) {
    if (!search && !status) {
      return <p className="result-summary">No customers yet.</p>;
    }

    return (
      <p className="result-summary">
        {search
          ? `No ${kind(0)} have a name or email starting with "${search}".`
          : `No ${kind(0)}.`}
      </p>
    );
  }

  if (shown === 0) {
    return (
      <p className="result-summary">
        Page {page} is past the end: there are {total} {kind(total)}
        {starting}.
      </p>
    );
  }

  const first = (page - 1) * pageSize + 1;
  const last = first + shown - 1;
  return (
    <p className="result-summary">
      {first === 1 && last === total
        ? `${total} ${kind(total)}`
        : `${first} to ${last} of ${total} ${kind(total)}`}
      {starting}
    </p>
  );
}

// On a phone, the whole card is the link: a big target, and no table to scroll sideways.
// Sorted by a date, each card shows that date, so the order makes sense.
function CustomerCards({
  customers,
  sort,
}: {
  customers: CustomerListItem[];
  sort: string;
}) {
  // A deleted customer's card says when, whatever the sort: it's why it's in this list.
  const dateOf = (customer: CustomerListItem) =>
    customer.deletedAt
      ? `Deleted ${formatUtcDate(customer.deletedAt)}`
      : sort === "updated"
        ? `Updated ${formatUtcDate(customer.updatedAt)}`
        : sort === "created"
          ? `Created ${formatUtcDate(customer.createdAt)}`
          : null;

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
              <span className="card-list-line">
                <Email address={customer.email} />
              </span>
            )}
            {customer.phone && (
              <span className="card-list-line">{customer.phone}</span>
            )}
            {dateOf(customer) && (
              <span className="card-list-line">{dateOf(customer)}</span>
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
