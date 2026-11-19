import { Fragment } from "react";
import { emailParts } from "./emailParts";

// An email address that wraps only before a dot or after the @ (emailParts.ts says why), never
// in the middle of a word. Copying it still gives the plain address: <wbr> adds no characters.
export function Email({ address }: { address: string }) {
  return (
    <span className="email">
      {emailParts(address).map((part, index) => (
        <Fragment key={index}>
          {index > 0 && <wbr />}
          {part}
        </Fragment>
      ))}
    </span>
  );
}
