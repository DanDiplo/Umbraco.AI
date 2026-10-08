# Build log

- 08-10-2026 T1 dcb0e2a8: usage model types. Reviewer PASS; rebuilt + reran unit tests (1323 passed, 0 failed, 37 skipped pending). No entry point to smoke (types only; persisted via existing JSON column, verified by round-trip spec).
- 08-10-2026 T2 180c781e: internal ambient usage collector. Review FAIL once (non-nullable provider/model IDs), fixed, then PASS; reviewer reran unit tests (1339 passed, 0 failed, 27 skipped pending). No entry point yet (wired in T3/T4).
