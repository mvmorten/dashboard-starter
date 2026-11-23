import type { ReactNode } from "react";

// What the input inside a field gets from it: its id, and the ARIA attributes that tie it to
// the field's hint, error and anything else that describes it. A screen reader reads them all
// when the input gets focus.
export interface ControlProps {
  id: string;
  "aria-invalid": true | undefined;
  "aria-describedby": string | undefined;
}

export interface FieldProps {
  id: string;
  label: string;
  // Adds "(optional)" to the label. Fields are required unless they say so.
  optional?: boolean;
  // Said before the input, so it's read before you start typing.
  hint?: string;
  error?: string;
  // More after the error (the character count), and the id of the part that describes the
  // input while it's showing.
  footer?: ReactNode;
  footerId?: string;
  children: (control: ControlProps) => ReactNode;
}

// One form field: the label, the "(optional)" marker, the hint, the input, and the error, with
// the id wiring between them. Each kind of field (TextField, SelectField, ...) is this, with its
// own input inside.
export default function Field({
  id,
  label,
  optional,
  hint,
  error,
  footer,
  footerId,
  children,
}: FieldProps) {
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [
    hint ? hintId : null,
    error ? errorId : null,
    footerId ?? null,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {optional && (
          <>
            {" "}
            <span className="optional">(optional)</span>
          </>
        )}
      </label>
      {hint && (
        <p id={hintId} className="field-hint">
          {hint}
        </p>
      )}
      {children({
        id,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": describedBy || undefined,
      })}
      {error && (
        <p id={errorId} className="field-error">
          {error}
        </p>
      )}
      {footer}
    </div>
  );
}
