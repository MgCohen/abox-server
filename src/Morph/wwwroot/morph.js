export function prefersReducedMotion() {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function countItems(stage) {
    return stage.querySelectorAll(".morph-item").length;
}

export function registerMorphEvents() {
    Blazor.registerCustomEventType("morphend", {
        browserEventName: "animationend",
        createEventArgs: (event) => ({
            isItem: event.target.classList.contains("morph-item"),
        }),
    });
}
