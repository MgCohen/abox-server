export const meta = {
  name: 'judge',
  description: 'Generic rubric-driven judge: grades an artifact against supplied Criteria, returns per-criterion verdicts + a deterministic score.',
  phases: [{ title: 'Judge' }],
}

// Output schema is DERIVED from the criteria: exactly one result per id, ids enum-bound.
function deriveSchema(criteria) {
  const ids = criteria.map(c => c.id)
  return {
    type: 'object',
    properties: {
      results: {
        type: 'array', minItems: ids.length, maxItems: ids.length,
        items: {
          type: 'object',
          properties: {
            criterionId: { type: 'string', enum: ids },
            status: { type: 'string', enum: ['pass', 'fail', 'indeterminate'] },
            evidence: { type: 'string' },
          },
          required: ['criterionId', 'status', 'evidence'],
        },
      },
    },
    required: ['results'],
  }
}

// Deterministic rollup — never the model. Indeterminate penalizes (counts in total, not passed).
function scoreOf(results) {
  const n = s => results.filter(r => r.status === s).length
  const passed = n('pass'), failed = n('fail'), indeterminate = n('indeterminate')
  const total = results.length
  return {
    passed, failed, indeterminate, total,
    score10: Math.round((10 * passed) / total),
    overallPass: failed === 0 && indeterminate === 0,
  }
}

function renderRequest(r) {
  return `Subject: ${r.subject}

Context (use this first):
${r.context}

Supporting files (read only if a criterion can't be assessed from the context above):
${(r.files || []).map(p => `- ${p}`).join('\n')}

Criteria (one result per id):
${r.criteria.map((c, i) => `${i + 1}. [${c.id}] ${c.description}${c.howToCheck ? ` — check: ${c.howToCheck}` : ''}`).join('\n')}`
}

phase('Judge')
const input = typeof args === 'string' ? JSON.parse(args) : args
const requests = Array.isArray(input) ? input : [input]
const verdicts = await parallel(requests.map(r => async () => {
  const v = await agent(renderRequest(r), { agentType: 'judge', schema: deriveSchema(r.criteria) })
  if (!v) return null
  const want = new Set(r.criteria.map(c => c.id))
  const got = v.results.map(x => x.criterionId)
  if (got.length !== want.size || new Set(got).size !== got.length || got.some(id => !want.has(id)))
    throw new Error(`judge returned mismatched criterion ids for "${r.subject}"`)
  return { subject: r.subject, ...v, ...scoreOf(v.results) }
}))
const out = verdicts.filter(Boolean)
return out.length === 1 ? out[0] : out
