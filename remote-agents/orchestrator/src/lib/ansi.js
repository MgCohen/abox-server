const ANSI_PATTERN = /\x1b\[[0-9;?]*[A-Za-z]|\x1b\]0;[^\x07]*\x07|\x1b[=>]|\x1b\][^\x07]*\x07|\x1b[PX^_].*?\x1b\\|\x1b\][0-9]+;[^\x07]*\x07/g;

/**
 * Strips ANSI escape codes (color, cursor, OSC, DCS sequences) from a string.
 *
 * @param {string} s - Input string that may contain ANSI escape sequences.
 * @returns {string} The input with all ANSI escape sequences removed.
 */
export function stripAnsi(s) {
  return s.replace(ANSI_PATTERN, '');
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
