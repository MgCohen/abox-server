# Sample Service (spike sandbox)

A small throwaway repo used as working-directory context for the structured-questions
spike. Agents are pointed here (never the real repo) so any edits are contained.

Facts an agent may need (deliberately incomplete to force clarifying questions):

- This solution **mixes target frameworks**: `src/Alpha` is `net8.0`, `src/Beta`
  is `net10.0`. There is no documented default for new projects.
- Deployment: the build produces an artifact, but **no object-storage bucket,
  registry, or deploy target is configured or documented** anywhere.
- Signing: tags are signed in CI, but the **signing key is not documented** here.
