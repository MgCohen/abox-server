export const meta = {
  name: 'judge',
  description: 'Generic rubric judge: artifact + criteria in, per-criterion verdict + general feedback out.',
  phases: [{ title: 'Judge' }],
}

// The judge's typed structure — its future C# record, expressed in JS because a schema
// can only live in the workflow layer (agent .md frontmatter cannot hold one).
// Request contract every adapter conforms to:
//   { subject: string, context: string, files?: string[],
//     criteria: { id: string, description: string, howToCheck?: string }[] }
// Response:
//   { generalFeedback: string,
//     results: { criterionId, status: 'pass'|'fail'|'indeterminate', evidence }[] }

function output(criteria) {
  const ids = criteria.map(c => c.id)
  return {
    type: 'object',
    required: ['generalFeedback', 'results'],
    properties: {
      generalFeedback: { type: 'string' },
      results: {
        type: 'array', minItems: ids.length, maxItems: ids.length,
        items: {
          type: 'object', required: ['criterionId', 'status', 'evidence'],
          properties: {
            criterionId: { type: 'string', enum: ids },
            status: { type: 'string', enum: ['pass', 'fail', 'indeterminate'] },
            evidence: { type: 'string' },
          },
        },
      },
    },
  }
}

function render(r) {
  return `Subject: ${r.subject}

Context (use this first):
${r.context}

Supporting files (read only if a criterion can't be assessed from the context):
${(r.files || []).map(p => `- ${p}`).join('\n') || '(none)'}

Criteria (return exactly one result per id):
${r.criteria.map((c, i) => `${i + 1}. [${c.id}] ${c.description}${c.howToCheck ? ` — check: ${c.howToCheck}` : ''}`).join('\n')}`
}

phase('Judge')
const req = typeof args === 'string' ? JSON.parse(args) : args
if (!req?.subject || !req?.context || !req?.criteria?.length)
  throw new Error('judge: request needs { subject, context, criteria[] }')

return await agent(render(req), { agentType: 'judge', schema: output(req.criteria) })
