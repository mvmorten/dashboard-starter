import { useQueryClient } from "@tanstack/react-query";
import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type RefObject,
} from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { currentTenant } from "../auth/me";
import { useMe } from "../auth/useMe";
import {
  createCustomer,
  currentOf,
  customerFieldLabels,
  customerQueryKey,
  customersQueryKey,
  deleteCustomer,
  emptyCustomerFields,
  fieldErrorsOf,
  fieldsOf,
  sameFields,
  trimFields,
  updateCustomer,
  type Customer,
  type CustomerField,
  type CustomerFields,
} from "../customers/customers";
import { mergeThreeWay } from "../customers/merge";
import { useCustomer } from "../customers/useCustomers";
import { formatUtc } from "../format";
import { usePageTitle } from "../layout/usePageTitle";
import { useUnsavedChangesWarning } from "../layout/useUnsavedChangesWarning";
import type { CustomersNotice } from "./Customers";
import NoAccess from "./NoAccess";
import NotFound from "./NotFound";

// /customers/new and /customers/{id}. With customers.edit, a form; without it, the same fields
// to read. The API checks every rule again; nothing here is the security boundary.
export default function Customer() {
  const { id } = useParams();
  const { data: me } = useMe();

  if (!can(me, Permissions.customersView)) {
    return <NoAccess />;
  }

  if (id === undefined) {
    return can(me, Permissions.customersEdit) ? (
      <CustomerEditor key="new" initial={null} />
    ) : (
      <NoAccess />
    );
  }

  const customerId = Number(id);
  if (!Number.isInteger(customerId) || customerId < 1) {
    return <NotFound />;
  }

  // A new tenant, or another customer, starts the page over, unsaved edits and all.
  return (
    <ExistingCustomer
      key={`${me?.currentTenantId}-${customerId}`}
      id={customerId}
    />
  );
}

function ExistingCustomer({ id }: { id: number }) {
  const { data: me } = useMe();
  const customer = useCustomer(id);
  usePageTitle(customer.data?.name ?? "Customer");

  // Once loaded, the editor keeps its own copy. A later refetch that fails (someone deleted it)
  // must not take the form away from someone typing: the save will say what happened.
  if (customer.data) {
    return can(me, Permissions.customersEdit) ? (
      <CustomerEditor initial={customer.data} />
    ) : (
      <CustomerView customer={customer.data} />
    );
  }

  if (customer.isPending) {
    return <p role="status">Loading...</p>;
  }

  if (customer.error instanceof ApiError && customer.error.status === 403) {
    return <NoAccess />;
  }

  if (customer.error instanceof ApiError && customer.error.status === 404) {
    return (
      <>
        <BackLink />
        <h1>No such customer</h1>
        <p className="lead">
          It may have been deleted, or it belongs to another tenant.
        </p>
      </>
    );
  }

  return (
    <div role="alert">
      <BackLink />
      <h1>Customer</h1>
      <p className="lead">The server didn&apos;t answer.</p>
      <button type="button" onClick={() => void customer.refetch()}>
        Try again
      </button>
    </div>
  );
}

function BackLink() {
  return (
    <p className="back-link">
      <Link to="/customers">All customers</Link>
    </p>
  );
}

function LastChanged({ customer }: { customer: Customer }) {
  return (
    <p className="lead">
      Last changed {formatUtc(customer.updatedAt)}
      {customer.updatedByName && ` by ${customer.updatedByName}`}.
    </p>
  );
}

// Without customers.edit: the same fields, to read.
function CustomerView({ customer }: { customer: Customer }) {
  const fields = fieldsOf(customer);
  return (
    <>
      <BackLink />
      <h1>{customer.name}</h1>
      <LastChanged customer={customer} />
      <dl className="field-list">
        {(Object.keys(customerFieldLabels) as CustomerField[]).map((field) => (
          <div key={field}>
            <dt>{customerFieldLabels[field]}</dt>
            <dd>
              <FieldValue value={fields[field]} />
            </dd>
          </div>
        ))}
      </dl>
    </>
  );
}

function FieldValue({ value }: { value: string }) {
  return value === "" ? (
    <span className="empty-value">(empty)</span>
  ) : (
    <span className="field-value">{value}</span>
  );
}

// Someone else saved while this person was editing (the API said 409). Everything the form
// needs to show the three-way merge (customers/merge.ts) and let them choose.
interface MergeState {
  // Their version: what the form now builds on, so the next save sends its rowVersion.
  theirs: Customer;
  original: CustomerFields;
  mine: CustomerFields;
  takenFromTheirs: CustomerField[];
  conflicts: CustomerField[];
  // Per conflict, which version the person picked. Saving waits until every one is picked.
  choices: Partial<Record<CustomerField, "mine" | "theirs">>;
  // The 409 answered a delete, not a save.
  onDelete: boolean;
}

function CustomerEditor({ initial }: { initial: Customer | null }) {
  usePageTitle(initial?.name ?? "New customer");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: me } = useMe();
  const tenantId = me?.currentTenantId ?? null;
  const arrivedWith = (useLocation().state as CustomersNotice | null)?.notice;

  // The version these edits build on: what the form loaded, then whatever the last save (or
  // the last 409) returned. Null for a new customer.
  const [base, setBase] = useState<Customer | null>(initial);
  const [values, setValues] = useState<CustomerFields>(
    initial ? fieldsOf(initial) : emptyCustomerFields,
  );
  const [errors, setErrors] = useState<Partial<Record<CustomerField, string>>>(
    {},
  );
  const [formError, setFormError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(arrivedWith ?? null);
  const [merge, setMerge] = useState<MergeState | null>(null);
  // Someone deleted it while this person was editing (the save said 404).
  const [gone, setGone] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [pending, setPending] = useState<"save" | "delete" | null>(null);
  const headsUp = useRef<HTMLElement>(null);
  const confirmBox = useRef<HTMLDivElement>(null);

  const unsaved =
    gone || !sameFields(values, base ? fieldsOf(base) : emptyCustomerFields);
  useUnsavedChangesWarning(unsaved && pending === null);

  // Move focus to what just appeared, so keyboard and screen reader users land on it.
  useEffect(() => {
    if (merge || gone) {
      headsUp.current?.focus();
    }
  }, [merge, gone]);

  useEffect(() => {
    if (confirmingDelete) {
      confirmBox.current?.focus();
    }
  }, [confirmingDelete]);

  function setField(field: CustomerField, value: string) {
    setValues((current) => ({ ...current, [field]: value }));
  }

  function focusField(field: CustomerField) {
    document.getElementById(`customer-${field}`)?.focus();
  }

  // The list and this customer's cached copy are out of date after any change.
  function refreshCached(saved: Customer | null) {
    if (saved) {
      queryClient.setQueryData(customerQueryKey(tenantId, saved.id), saved);
    }

    void queryClient.invalidateQueries({ queryKey: customersQueryKey });
  }

  function showConflict(theirs: Customer, onDelete: boolean) {
    // Each save so far moved `base` forward, so it's the version both edits started from.
    const original = fieldsOf(base!);
    const mine = trimFields(values);
    const result = mergeThreeWay(original, mine, fieldsOf(theirs));

    setBase(theirs);
    setValues(result.merged);
    setErrors({});
    setConfirmingDelete(false);
    setMerge({
      theirs,
      original,
      mine,
      takenFromTheirs: result.takenFromTheirs,
      conflicts: result.conflicts,
      choices: {},
      onDelete,
    });
    refreshCached(theirs);
  }

  function choose(field: CustomerField, choice: "mine" | "theirs") {
    if (!merge) {
      return;
    }

    setMerge({ ...merge, choices: { ...merge.choices, [field]: choice } });
    setField(
      field,
      choice === "mine" ? merge.mine[field] : fieldsOf(merge.theirs)[field],
    );
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // Errors from the last try don't apply any more; this save brings its own, if any.
    setErrors({});
    setFormError(null);
    setNotice(null);

    const unchosen = merge?.conflicts.filter((field) => !merge.choices[field]);
    if (unchosen && unchosen.length > 0) {
      setFormError(
        `Choose yours or theirs for ${listOf(unchosen.map((field) => customerFieldLabels[field]))} first.`,
      );
      document.getElementById(`conflict-${unchosen[0]}-mine`)?.focus();
      return;
    }

    if (base && !unsaved) {
      setMerge(null);
      setNotice("Nothing to save: no changes.");
      return;
    }

    const fields = trimFields(values);
    setPending("save");
    try {
      if (!base) {
        const created = await createCustomer(fields);
        refreshCached(created);
        navigate(`/customers/${created.id}`, {
          replace: true,
          state: {
            notice: "Customer added.",
            tenantId,
          } satisfies CustomersNotice,
        });
        return;
      }

      const saved = await updateCustomer(base.id, fields, base.rowVersion);
      setBase(saved);
      setValues(fieldsOf(saved));
      setErrors({});
      setMerge(null);
      setNotice("Saved.");
      refreshCached(saved);
    } catch (caught) {
      handleSaveFailure(caught);
    } finally {
      setPending(null);
    }
  }

  function handleSaveFailure(caught: unknown) {
    const fieldErrors = fieldErrorsOf(caught);
    if (fieldErrors) {
      setErrors(fieldErrors);
      const first = (Object.keys(customerFieldLabels) as CustomerField[]).find(
        (field) => fieldErrors[field],
      );
      if (first) {
        focusField(first);
      }

      return;
    }

    const theirs = currentOf(caught);
    if (theirs) {
      showConflict(theirs, false);
      return;
    }

    if (base && caught instanceof ApiError && caught.status === 404) {
      setMerge(null);
      setGone(true);
      return;
    }

    setFormError(messageOf(caught));
  }

  // Deleted by someone else: this person's version only exists in the form now.
  async function saveAsNew() {
    setErrors({});
    setFormError(null);
    setPending("save");
    try {
      const created = await createCustomer(trimFields(values));
      refreshCached(created);
      navigate(`/customers/${created.id}`, {
        replace: true,
        state: {
          notice: "Saved as a new customer.",
          tenantId,
        } satisfies CustomersNotice,
      });
    } catch (caught) {
      const fieldErrors = fieldErrorsOf(caught);
      if (fieldErrors) {
        setErrors(fieldErrors);
      }

      setFormError(
        fieldErrors ? "Some fields need another look." : messageOf(caught),
      );
    } finally {
      setPending(null);
    }
  }

  async function confirmDelete() {
    if (!base) {
      return;
    }

    setFormError(null);
    setPending("delete");
    try {
      await deleteCustomer(base.id, base.rowVersion);
      refreshCached(null);
      navigate("/customers", {
        state: {
          notice: `Deleted: ${base.name}`,
          tenantId,
        } satisfies CustomersNotice,
      });
    } catch (caught) {
      const theirs = currentOf(caught);
      if (theirs) {
        showConflict(theirs, true);
        return;
      }

      if (caught instanceof ApiError && caught.status === 404) {
        refreshCached(null);
        navigate("/customers", {
          state: {
            notice: `${base.name} was already deleted.`,
            tenantId,
          } satisfies CustomersNotice,
        });
        return;
      }

      setFormError(messageOf(caught));
    } finally {
      setPending(null);
    }
  }

  const tenant = me ? currentTenant(me) : null;
  const errorText = (field: CustomerField) =>
    errors[field] ? (
      <p id={`customer-${field}-error`} className="field-error">
        {errors[field]}
      </p>
    ) : null;
  // The bits every input shares: its id, its value, and its error, if any.
  const inputProps = (field: CustomerField) => ({
    id: `customer-${field}`,
    name: field,
    value: values[field],
    "aria-invalid": errors[field] ? true : undefined,
    "aria-describedby": errors[field] ? `customer-${field}-error` : undefined,
  });

  return (
    <>
      <BackLink />
      <h1>{base?.name ?? "New customer"}</h1>
      {base ? (
        <LastChanged customer={base} />
      ) : (
        <p className="lead">
          A new customer in {tenant?.name ?? "this tenant"}.
        </p>
      )}

      <div role="status">{notice && <p className="notice">{notice}</p>}</div>

      {merge && (
        <MergePanel merge={merge} headingRef={headsUp} onChoose={choose} />
      )}

      {gone && (
        <section
          className="heads-up"
          aria-labelledby="gone-heading"
          ref={headsUp}
          tabIndex={-1}
        >
          <h2 id="gone-heading">Someone deleted this customer</h2>
          <p>
            It was deleted while you were editing it, so your changes
            weren&apos;t saved. They&apos;re still in the form below. Save them
            as a new customer, or leave them.
          </p>
        </section>
      )}

      <form method="post" className="customer-form" onSubmit={save} noValidate>
        <div className="field">
          <label htmlFor="customer-name">Name</label>
          <input
            {...inputProps("name")}
            type="text"
            autoComplete="off"
            onChange={(event) => setField("name", event.target.value)}
          />
          {errorText("name")}
        </div>

        <div className="field">
          <label htmlFor="customer-email">
            Email <span className="optional">(optional)</span>
          </label>
          <input
            {...inputProps("email")}
            type="email"
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            onChange={(event) => setField("email", event.target.value)}
          />
          {errorText("email")}
        </div>

        <div className="field">
          <label htmlFor="customer-phone">
            Phone <span className="optional">(optional)</span>
          </label>
          <input
            {...inputProps("phone")}
            type="tel"
            autoComplete="off"
            onChange={(event) => setField("phone", event.target.value)}
          />
          {errorText("phone")}
        </div>

        <div className="field">
          <label htmlFor="customer-status">Status</label>
          <select
            {...inputProps("status")}
            onChange={(event) => setField("status", event.target.value)}
          >
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
          {errorText("status")}
        </div>

        <div className="field">
          <label htmlFor="customer-notes">
            Notes <span className="optional">(optional)</span>
          </label>
          <textarea
            {...inputProps("notes")}
            rows={4}
            onChange={(event) => setField("notes", event.target.value)}
          />
          {errorText("notes")}
        </div>

        <div role="alert" className="form-error">
          {formError}
        </div>

        {gone ? (
          <div className="form-actions">
            <button
              type="button"
              className="primary-button"
              disabled={pending !== null}
              onClick={() => void saveAsNew()}
            >
              {pending === "save" ? "Saving..." : "Save as a new customer"}
            </button>
            <Link to="/customers" className="button-link">
              Leave without saving
            </Link>
          </div>
        ) : (
          <div className="form-actions">
            <button
              type="submit"
              className="primary-button"
              disabled={pending !== null}
            >
              {pending === "save"
                ? "Saving..."
                : base
                  ? merge
                    ? "Save again"
                    : "Save"
                  : "Add customer"}
            </button>
            <Link to="/customers" className="button-link">
              Cancel
            </Link>
          </div>
        )}
      </form>

      {base && !gone && (
        <DeleteSection
          name={base.name}
          unsaved={unsaved}
          confirming={confirmingDelete}
          pending={pending}
          boxRef={confirmBox}
          onStart={() => setConfirmingDelete(true)}
          onCancel={() => setConfirmingDelete(false)}
          onConfirm={() => void confirmDelete()}
        />
      )}
    </>
  );
}

function MergePanel({
  merge,
  headingRef,
  onChoose,
}: {
  merge: MergeState;
  headingRef: RefObject<HTMLElement | null>;
  onChoose: (field: CustomerField, choice: "mine" | "theirs") => void;
}) {
  const who = merge.theirs.updatedByName ?? "Someone else";
  const theirs = fieldsOf(merge.theirs);
  const nothingToDecide =
    merge.conflicts.length === 0 && merge.takenFromTheirs.length === 0;

  return (
    <section
      className="heads-up"
      aria-labelledby="merge-heading"
      ref={headingRef}
      tabIndex={-1}
    >
      <h2 id="merge-heading">
        {who} changed this customer while you were{" "}
        {merge.onDelete ? "deleting it" : "editing"}
      </h2>

      {merge.onDelete ? (
        <p>
          Nothing was deleted. Check what changed below, then delete it again if
          you still want to.
        </p>
      ) : nothingToDecide ? (
        <p>
          Your changes are still in the form, on top of theirs. Save again to
          keep them.
        </p>
      ) : (
        <p>
          Nothing of yours was lost: your changes are still in the form, on top
          of theirs. Check it, then save again.
        </p>
      )}

      {merge.takenFromTheirs.length > 0 && (
        <>
          <p>
            {merge.onDelete
              ? "They changed:"
              : "Their changes, now in the form:"}
          </p>
          <ul className="taken-list">
            {merge.takenFromTheirs.map((field) => (
              <li key={field}>
                <strong>{customerFieldLabels[field]}:</strong>{" "}
                <FieldValue value={theirs[field]} />
              </li>
            ))}
          </ul>
        </>
      )}

      {merge.conflicts.length > 0 && (
        <>
          <p>
            You both changed{" "}
            {merge.conflicts.length === 1
              ? "one field"
              : `${merge.conflicts.length} fields`}
            . Choose which version to keep:
          </p>
          {merge.conflicts.map((field) => (
            <fieldset key={field} className="conflict">
              <legend>{customerFieldLabels[field]}</legend>
              <p className="conflict-before">
                Before either change:{" "}
                <FieldValue value={merge.original[field]} />
              </p>
              <div className="conflict-options">
                <label className="conflict-option">
                  <input
                    type="radio"
                    id={`conflict-${field}-mine`}
                    name={`conflict-${field}`}
                    checked={merge.choices[field] === "mine"}
                    onChange={() => onChoose(field, "mine")}
                  />
                  <span className="conflict-who">Yours</span>
                  <FieldValue value={merge.mine[field]} />
                </label>
                <label className="conflict-option">
                  <input
                    type="radio"
                    name={`conflict-${field}`}
                    checked={merge.choices[field] === "theirs"}
                    onChange={() => onChoose(field, "theirs")}
                  />
                  <span className="conflict-who">Theirs ({who})</span>
                  <FieldValue value={theirs[field]} />
                </label>
              </div>
            </fieldset>
          ))}
        </>
      )}
    </section>
  );
}

function DeleteSection({
  name,
  unsaved,
  confirming,
  pending,
  boxRef,
  onStart,
  onCancel,
  onConfirm,
}: {
  name: string;
  unsaved: boolean;
  confirming: boolean;
  pending: "save" | "delete" | null;
  boxRef: RefObject<HTMLDivElement | null>;
  onStart: () => void;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  if (!confirming) {
    return (
      <div className="delete-section">
        <button
          type="button"
          className="danger-button"
          disabled={pending !== null}
          onClick={onStart}
        >
          Delete customer
        </button>
      </div>
    );
  }

  return (
    <div
      className="delete-section delete-confirm"
      ref={boxRef}
      tabIndex={-1}
      role="group"
      aria-labelledby="delete-question"
    >
      <p id="delete-question">
        <strong>Delete {name}?</strong> It disappears from the list for everyone
        in this tenant.
        {unsaved && " Your unsaved changes go with it."}
      </p>
      <div className="form-actions">
        <button
          type="button"
          className="danger-button danger-button-solid"
          disabled={pending !== null}
          onClick={onConfirm}
        >
          {pending === "delete" ? "Deleting..." : "Delete"}
        </button>
        <button type="button" disabled={pending !== null} onClick={onCancel}>
          Keep it
        </button>
      </div>
    </div>
  );
}

function listOf(items: string[]): string {
  if (items.length <= 1) {
    return items.join("");
  }

  return `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`;
}

function messageOf(caught: unknown): string {
  if (caught instanceof ApiError) {
    return caught.detail ?? caught.title ?? "That didn't work.";
  }

  return "The server didn't answer. Try again in a moment.";
}
