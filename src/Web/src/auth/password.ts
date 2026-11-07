// Forgot, reset, and change password. The rules for a new password live in the API
// (PasswordRules in src/Db); the length is repeated here only so the form can say it up front
// and catch a short password before a round trip. The API has the final word.

import { apiPost, refreshAntiforgeryToken } from "../api";

export const passwordMinLength = 12;
export const passwordMaxLength = 128;
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

// The checks the form can make on its own. Returns what's wrong, or null.
export function checkNewPassword(
  password: string,
  confirmation: string,
): string | null {
  if (password.length < passwordMinLength) {
    return `The new password must be at least ${passwordMinLength} characters.`;
  }

  if (password.length > passwordMaxLength) {
    return `The new password can be at most ${passwordMaxLength} characters.`;
  }

  if (password !== confirmation) {
    return "The two new passwords don't match.";
  }

  return null;
}
