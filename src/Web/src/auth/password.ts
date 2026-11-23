// Forgot, reset, and change password: the calls, and the hint a new password field shows. The
// rules for a new password are in passwordRules.ts, a copy of PasswordRules in src/Db.

import { apiPost, refreshAntiforgeryToken } from "../api";
import { passwordMaxLength, passwordMinLength } from "./passwordRules";

export const passwordHint = `${passwordMinLength} to ${passwordMaxLength} characters. A few unrelated words make a good one.`;

// Always answers the same, whether or not the address has an account.
export function forgotPassword(email: string): Promise<void> {
  return apiPost<void>("/api/auth/forgot-password", { email });
}

export function resetPassword(
  token: string,
  newPassword: string,
): Promise<void> {
  return apiPost<void>("/api/auth/reset-password", { token, newPassword });
}

export async function changePassword(
  currentPassword: string,
  newPassword: string,
): Promise<void> {
  await apiPost<void>("/api/auth/change-password", {
    currentPassword,
    newPassword,
  });
  // The API reissued this session's cookie with a new security stamp, and the antiforgery
  // token is tied to the cookie it was issued with.
  await refreshAntiforgeryToken();
}

// Not a password rule (the server never sees the second copy): a form check that the two new
// passwords are the same, so a typo doesn't lock anyone out.
export const passwordsDontMatch = "The two new passwords don't match.";
