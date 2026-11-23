import type { RefObject } from "react";

// Delete, behind a confirm step that says what happens.
export default function DeleteSection({
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
