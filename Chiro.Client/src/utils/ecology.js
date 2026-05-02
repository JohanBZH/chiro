/**
 * Calculates an endangerment score based on UICN Red List status codes.
 * Higher score means more endangered.
 * @param {Array<{code: string}>} statuses - List of status objects with a 'code' property.
 * @param {boolean} isDeterminant - Whether the species is determinant (expert selection).
 * @returns {number} The calculated score.
 */
export function calculateEndangermentScore(statuses, isDeterminant = false) {
  let maxScore = 0;

  if (statuses && Array.isArray(statuses)) {
    statuses.forEach((st) => {
      const code = (st.code || '').toUpperCase();
      if (code.includes("CR")) maxScore = Math.max(maxScore, 5);
      else if (code.includes("EN")) maxScore = Math.max(maxScore, 4);
      else if (code.includes("VU")) maxScore = Math.max(maxScore, 3);
      else if (code.includes("NT")) maxScore = Math.max(maxScore, 2);
      else if (code.includes("LC") || code.includes("LR/LC")) maxScore = Math.max(maxScore, 1);
    });
  }

  // If no Red List status but species is determinant, give it a base priority score
  if (maxScore === 0 && isDeterminant) {
    maxScore = 1.5;
  }

  return maxScore;
}
