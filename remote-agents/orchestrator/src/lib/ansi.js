const ANSI_PATTERN = /\x1b\[[0-9;?]*[A-Za-z]|\x1b\]0;[^\x07]*\x07|\x1b[=>]|\x1b\][^\x07]*\x07|\x1b[PX^_].*?\x1b\\|\x1b\][0-9]+;[^\x07]*\x07/g;

export function stripAnsi(s) {
  return s.replace(ANSI_PATTERN, '');
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
