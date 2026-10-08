# Decision log

- 08-10-2026: Routed #516 to the full pipeline, not quick-fix. A quick fix could only surface Prompt's single-call usage and would miss the "sum across all calls" requirement.
- 08-10-2026: Grader-made model calls are excluded from a run's usage. They are the cost of grading, not of the thing under test.
- 08-10-2026: Cost/CO2e calculation and a built-in budget grader are out of scope. This supplies inputs only.
- 08-10-2026: Collect usage at the operation tracker via an internal AsyncLocal scope, not by changing IAITestFeature. One seam covers every feature; no public interface change.
- 08-10-2026: Per-model breakdown lives on AITestTokenUsage. Graders get it through the existing outcome argument; it persists in the existing JSON column, so no migration.
- 08-10-2026: Resolved profile is exposed per model entry, not as a new run column (avoids a migration).
- 08-10-2026: A call with no usage counts as "unreported", not zero, so budget graders can tell totals are incomplete.
- 08-10-2026: Errored runs keep today's behavior (no outcome, no usage). No consumer for partial usage yet.
- 08-10-2026: The collector emits its own internal snapshot, mapped to AITestTokenUsage by the runner. Keeps Observability free of a dependency on Tests, so T2 can run alongside T1.
- 08-10-2026: T4 (runner wiring) waits on T3 (tracker wiring) so runner tests exercise the real tracked path, not a stub.
