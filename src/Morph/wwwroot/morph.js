export function prefersReducedMotion() {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function waitForAnimations(el) {
    return new Promise((resolve) => requestAnimationFrame(() => {
        const anims = el.getAnimations({ subtree: true }).filter((a) =>
            a.effect?.getComputedTiming().iterations !== Infinity &&
            a.effect?.target?.closest?.(".morph-item"));
        resolve(Promise.all(anims.map((a) => a.finished)));
    }));
}
