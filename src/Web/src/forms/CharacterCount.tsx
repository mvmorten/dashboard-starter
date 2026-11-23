import { useEffect, useRef, useState } from "react";
import { countMessage } from "./count";

// How long typing has to pause before a screen reader hears the count.
const announceAfterMs = 1000;

// "214 characters left", under a text area with a long limit, shown once the text gets near
// it (count.ts). Two copies of the same words:
// - The visible one isn't a live region, so it's never read out on every key. The text area
//   lists it in aria-describedby, so it's read with the field when it gets focus.
// - A visually hidden status region repeats it once typing pauses, so a screen reader user
//   hears it politely while writing. Not when the page opens: only after a change.
export default function CharacterCount({
  id,
  length,
  max,
}: {
  id: string;
  length: number;
  max: number;
}) {
  const message = countMessage(length, max);
  const [announced, setAnnounced] = useState<string | null>(null);
  const lastSeen = useRef(message);

  useEffect(() => {
    if (message === lastSeen.current) {
      return;
    }

    lastSeen.current = message;
    const timer = setTimeout(() => setAnnounced(message), announceAfterMs);
    return () => clearTimeout(timer);
  }, [message]);

  return (
    <>
      {message && (
        <p
          id={id}
          className={
            length > max ? "field-count field-count-over" : "field-count"
          }
        >
          {message}
        </p>
      )}
      <p role="status" className="visually-hidden">
        {announced}
      </p>
    </>
  );
}
