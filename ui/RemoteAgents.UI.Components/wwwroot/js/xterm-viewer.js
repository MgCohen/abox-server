// Thin Blazor JS interop around xterm.js. Terminal + FitAddon are loaded
// from global window.* (script tags in the host's index.html) so this
// module stays static-import-free and works under Blazor WebAssembly's
// IJSObjectReference.import() without a bundler.
//
// Why we don't sanitize before write: the chunks coming in are the raw
// PTY bytes (UTF-8) that the host's PtySession captured from claude.exe
// via ConPTY. xterm.js is a real VT100/xterm parser — handing it the
// raw escape sequences (alt-screen toggles, cursor positioning, color,
// erase-line) is exactly what makes spinners and tool boxes render.
// The previous regex-strip path is what produced the "Inieni\nnnng" mess.

const handles = new Map();
let nextId = 1;

export function create(element) {
    const Terminal = window.Terminal;
    if (!Terminal) {
        throw new Error("xterm.js not loaded — check index.html script tags");
    }

    const term = new Terminal({
        convertEol: false,
        cursorBlink: false,
        cursorStyle: "block",
        disableStdin: true,
        fontFamily: 'ui-monospace, "Cascadia Code", "JetBrains Mono", Menlo, Consolas, monospace',
        fontSize: 13,
        scrollback: 5000,
        theme: {
            background: "#0b0b0b",
            foreground: "#dcdcdc",
        },
    });

    let fit = null;
    if (window.FitAddon && window.FitAddon.FitAddon) {
        fit = new window.FitAddon.FitAddon();
        term.loadAddon(fit);
    }

    term.open(element);
    if (fit) {
        try { fit.fit(); } catch (_) { /* element not yet sized */ }
    }

    const id = nextId++;
    handles.set(id, { term, fit, element });
    return id;
}

// chunk is a string carrying the original PTY bytes UTF-8-decoded. JSON
// round-trip preserves all C0/C1 control bytes (ESC = 0x1B encoded as
// ), so the escape sequences arrive intact.
export function write(handle, chunk) {
    const h = handles.get(handle);
    if (!h) return;
    h.term.write(chunk);
}

export function fit(handle) {
    const h = handles.get(handle);
    if (!h || !h.fit) return;
    try { h.fit.fit(); } catch (_) { /* swallow */ }
}

export function clear(handle) {
    const h = handles.get(handle);
    if (!h) return;
    h.term.clear();
}

export function dispose(handle) {
    const h = handles.get(handle);
    if (!h) return;
    try { h.term.dispose(); } catch (_) { /* already disposed */ }
    handles.delete(handle);
}
