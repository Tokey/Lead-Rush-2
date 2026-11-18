# B66: Rate task difficulty in the contribution score

**Owner:** Codex (`gpt-6`)  
**Reviewer:** Claude  
**Status:** implementation in progress

## Research findings

The 2026 ESEM study by Al-Haque and Johnson estimates coding-agent task difficulty from fixed task, repository, and prompt features. Across 45,769 issue-resolution tasks, patch structure and repository structure were much more predictive than prompt text alone. The strongest signals included how scattered the required edits were and how large or wide the repository search space was. Prompt ambiguity mattered more for tasks near the middle of the difficulty range. The paper's model predicted whether a particular reference agent would succeed; its difficulty is therefore agent-relative, not a universal measure of human effort. [Paper and replication package](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.ESEM.2026.3)

Earlier work by Weiß et al. predicts bug-fix effort by comparing an issue description to similar issues with recorded effort. This supports calibrating against LR2's own history when enough comparable, consistently logged tasks exist. It does not make elapsed time a fair standalone difficulty measure for agents with different quotas, interruptions, and parallel work. [Paper](https://thomas-zimmermann.com/publications/files/weiss-wsr-2007.pdf)

## LR2 rating rubric

Rate a task from its request, existing architecture, and intended verification. Use these three observable dimensions, each scored 0, 1, or 2:

| Dimension | 0 | 1 | 2 |
|---|---|---|---|
| Scope and integration | One local component | Several files in one system | Multiple systems or a shared runtime/editor pipeline |
| Uncertainty | Known behavior and clear acceptance check | Some investigation or edge cases | Root cause, design, or integration path is substantially unknown |
| Verification burden | Build or narrow source check | Integrated check in one scene or mode | Visible multi-map, audio, animation-feel, or cross-mode verification |

Sum the dimensions: **0–2 small**, **3–4 standard**, **5–6 hard**. Store the band and a short evidence note with the task. A non-implementer assigns or confirms the rating. Never infer difficulty from the implementer's identity or from a successful/failed outcome.

This is a transparent LR2 approximation, not the paper's trained predictor: current task rows do not consistently contain pre-change repository snapshots or per-task patch features. Do not use raw lines changed or prompt length as a score by themselves. When at least 30 tasks have preregistered ratings and outcomes, compare success, review catches, escaped defects, and verified completion by band. Revise the rubric only from that evidence and preserve the prior scores for comparison.

### Existing task ledger: retrospective peer ratings

At the developer's request on 2026-11-18, three independent reviewers rated all 99 existing task entries from their task descriptions, tracker requests, and linked specs. They were instructed to ignore the implementer/model, outcome, and implementation. Each reviewer scored all three dimensions; the stored task band uses the **median of the three total scores**, then the rubric thresholds above. The row-level votes and consensus are recorded in [`../data/task_difficulty_reviews.csv`](../data/task_difficulty_reviews.csv). Six task records were marked under-specified by at least two reviewers; their estimates use only the stated scope and carry that note.

These are retrospective estimates of requested scope, not measurements of actual effort or a claim that the work was difficult for a particular model. They intentionally change the historical difficulty-adjusted score; the raw score remains alongside it for comparison. For future work, record the independent rating **before implementation** and set `difficulty_timing` to `pre-work-peer-consensus`. Use `retrospective-peer-consensus` for this audited historical batch. Missing or `unrated` values remain neutral at 1x.

## Score integration

Apply difficulty only to the existing task-completion credit:

| Band | Completion multiplier |
|---|---:|
| Small | 0.75x |
| Standard | 1.00x |
| Hard | 1.25x |
| Any unrated work | 1.00x |

The completion base remains `+1` for verified work and `+0.5` for work ready for review. Bug-finding, suggestions, and defect penalties keep their current weights, avoiding duplicate difficulty bonuses for the same outcome. Report both the original score and the difficulty-adjusted score, plus the peer-rated count and whether the ratings were retrospective or pre-work. An unrated task receives neutral 1x.

## Acceptance checks

- The ledger preserves existing rows and records `difficulty`, `difficulty_timing`, and `difficulty_basis`.
- A missing or `unrated` difficulty uses exactly the current score weight.
- Small/standard/hard rows use `0.75/1.00/1.25` only for verified/ready task credit.
- Bug, suggestion, escaped-defect, review, and pre-push terms remain unchanged.
- Generated charts and tables say whether higher or lower is better, disclose the rated-task count, and distinguish retrospective from pre-work ratings.
- The audit CSV preserves all three individual votes, consensus bands, and under-specification flags so the retrospective decision can be reviewed.
