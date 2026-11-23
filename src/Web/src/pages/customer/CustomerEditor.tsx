import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { ApiError } from "../../api";
import { currentTenant } from "../../auth/me";
import { useMe } from "../../auth/useMe";
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
} from "../../customers/customers";
import { mergeThreeWay } from "../../customers/merge";
import { usePageTitle } from "../../layout/usePageTitle";
import { useUnsavedChangesWarning } from "../../layout/useUnsavedChangesWarning";
import type { CustomersNotice } from "../Customers";
import DeleteSection from "./DeleteSection";
import MergePanel, { type MergeState } from "./MergePanel";
import { listOf, messageOf } from "./messages";
import { BackLink, LastChanged } from "./parts";

// The form for a new customer, or one being edited: saving, the three-way merge after a 409,
// "deleted while you were editing", and delete.
export default function CustomerEditor({
  initial,
}: {
  initial: Customer | null;
}) {
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
