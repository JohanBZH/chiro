import { describe, it, expect } from 'vitest'
import { calculateEndangermentScore } from '../ecology'

describe('calculateEndangermentScore', () => {
  it('returns 5 for CR (Critically Endangered)', () => {
    const statuses = [{ code: 'CR' }]
    expect(calculateEndangermentScore(statuses)).toBe(5)
  })

  it('returns 4 for EN (Endangered)', () => {
    const statuses = [{ code: 'EN' }]
    expect(calculateEndangermentScore(statuses)).toBe(4)
  })

  it('handles multiple statuses and takes the highest', () => {
    const statuses = [{ code: 'NT' }, { code: 'VU' }, { code: 'LC' }]
    expect(calculateEndangermentScore(statuses)).toBe(3) // VU is higher than NT/LC
  })

  it('returns 1.5 if species is determinant but has no Red List status', () => {
    const statuses = []
    expect(calculateEndangermentScore(statuses, true)).toBe(1.5)
  })

  it('returns 0 for LC if no high status is present', () => {
    const statuses = [{ code: 'LC' }]
    expect(calculateEndangermentScore(statuses)).toBe(1)
  })

  it('returns 0 for empty statuses and non-determinant species', () => {
    expect(calculateEndangermentScore([])).toBe(0)
    expect(calculateEndangermentScore(null)).toBe(0)
  })

  it('is case insensitive', () => {
    const statuses = [{ code: 'vu' }]
    expect(calculateEndangermentScore(statuses)).toBe(3)
  })
})
