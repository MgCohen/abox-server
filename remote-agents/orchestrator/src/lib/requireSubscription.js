export function requireSubscription() {
  const bad = [];
  if (process.env.ANTHROPIC_API_KEY) bad.push('ANTHROPIC_API_KEY');
  if (process.env.CLAUDE_API_KEY)    bad.push('CLAUDE_API_KEY');
  if (bad.length > 0) {
    throw new Error(
      `Refusing to start: ${bad.join(', ')} is set in the environment.\n` +
      `If set, the claude CLI bills against the API instead of the Max\n` +
      `subscription, defeating the point of this orchestrator. Unset it\n` +
      `and re-run, or use the apiProvider explicitly if you really want API mode.`,
    );
  }
}
