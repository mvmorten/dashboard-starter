import type { RefObject } from "react";
import {
  customerFieldLabels,
  fieldsOf,
  type Customer,
  type CustomerField,
  type CustomerFields,
} from "../../customers/customers";
import { FieldValue } from "./parts";

// Someone else saved while this person was editing (the API said 409). Everything the form
// needs to show the three-way merge (customers/merge.ts) and let them choose.
export interface MergeState {
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

export default function MergePanel({
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
