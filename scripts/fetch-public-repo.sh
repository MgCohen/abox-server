#!/usr/bin/env bash
#
# Read a PUBLIC GitHub repo that is outside this session's GitHub scope.
#
# `git clone` and the mcp__github tools are routed through the scoped Git Proxy
# (127.0.0.1) and return 401/403 for any repo other than the one this session is
# scoped to. Public, world-readable content does not need that authenticated
# path: codeload/raw.githubusercontent ride the general egress proxy, which Full
# (or default Trusted) network access already allows. This script uses that route.
#
#   fetch-public-repo.sh <owner/repo> [ref]                 -> extract tarball into ./<repo>
#   fetch-public-repo.sh <owner/repo> [ref] --to <dir>      -> extract tarball into <dir>
#   fetch-public-repo.sh <owner/repo>:<path> [ref]          -> print a single file to stdout
#
# ref defaults to the repo's default branch (tries main, then master).

set -euo pipefail

usage() { grep '^#' "$0" | sed 's/^# \?//'; exit "${1:-0}"; }
[ $# -ge 1 ] || usage 1
case "$1" in -h|--help) usage 0;; esac

spec="$1"; shift
ref=""; to=""
while [ $# -gt 0 ]; do
  case "$1" in
    --to) to="${2:?--to needs a directory}"; shift 2;;
    *) ref="$1"; shift;;
  esac
done

fetch_file() {
  local repo="$1" path="$2" r="$3" code
  for try in "${r:-main}" "${r:-master}"; do
    code=$(curl -sSL -w '%{http_code}' -o /tmp/fpr.out \
      "https://raw.githubusercontent.com/${repo}/${try}/${path}") || true
    if [ "$code" = "200" ]; then cat /tmp/fpr.out; rm -f /tmp/fpr.out; return 0; fi
    [ -n "$r" ] && break
  done
  echo "fetch-public-repo: ${repo}/${path}@${r:-main|master} -> HTTP ${code}" >&2
  rm -f /tmp/fpr.out; return 1
}

fetch_repo() {
  local repo="$1" r="$2" dest="$3" code tgz
  dest="${dest:-./${repo#*/}}"
  tgz=$(mktemp --suffix=.tgz)
  for try in "${r:-main}" "${r:-master}"; do
    code=$(curl -sSL -w '%{http_code}' -o "$tgz" \
      "https://codeload.github.com/${repo}/tar.gz/refs/heads/${try}") || true
    if [ "$code" = "200" ]; then
      mkdir -p "$dest"
      tar xz --strip-components=1 -C "$dest" -f "$tgz"
      rm -f "$tgz"
      echo "fetch-public-repo: ${repo}@${try} -> ${dest}" >&2
      return 0
    fi
    [ -n "$r" ] && break
  done
  echo "fetch-public-repo: ${repo}@${r:-main|master} -> HTTP ${code}" >&2
  rm -f "$tgz"; return 1
}

case "$spec" in
  */*:*) fetch_file "${spec%%:*}" "${spec#*:}" "$ref";;
  */*)   fetch_repo "$spec" "$ref" "$to";;
  *)     echo "fetch-public-repo: expected <owner/repo> or <owner/repo>:<path>, got '$spec'" >&2; exit 1;;
esac
