export const meta = {
  name: 'judge',
  description: 'Schema-enforced test judge — reuses the .claude/agents/judge.md persona and returns a validated verdict.',
  phases: [{ title: 'Judge' }],
}

const VERDICT = {
  type: 'object',
  properties: {
    target: { type: 'string' },
    overall_pass: { type: 'boolean' },
    score: { type: 'integer', minimum: 0, maximum: 10 },
    rulebook_compliance: {
      type: 'object',
      properties: {
        pass: { type: 'boolean' },
        findings: { type: 'array', items: { type: 'string' } },
      },
      required: ['pass', 'findings'],
    },
    faithfulness: {
      type: 'object',
      properties: {
        pass: { type: 'boolean' },
        checks: {
          type: 'array',
          items: {
            type: 'object',
            properties: {
              test: { type: 'string' },
              claims: { type: 'string' },
              verifies: { type: 'string' },
              faithful: { type: 'boolean' },
            },
            required: ['test', 'claims', 'verifies', 'faithful'],
          },
        },
      },
      required: ['pass', 'checks'],
    },
    faults: { type: 'array', items: { type: 'string' } },
    recommendations: { type: 'array', items: { type: 'string' } },
  },
  required: ['target', 'overall_pass', 'score', 'rulebook_compliance', 'faithfulness', 'faults', 'recommendations'],
}

phase('Judge')
const target = args || 'tests/Tests/Unit/Tests/FlowTests.cs'
const verdict = await agent(
  `Grade the test file at "${target}". Follow your judging procedure and return the verdict.`,
  { agentType: 'judge', schema: VERDICT }
)
return verdict
