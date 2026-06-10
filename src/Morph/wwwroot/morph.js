export function prefersReducedMotion() {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function countItems(stage) {
    return stage.querySelectorAll(".morph-item").length;
}
