// Previous and Next for a list the server pages (Customers). Moved here from Customers.tsx in
// the data grid step, unchanged, so the next list can use it too.
export function Pager({
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
