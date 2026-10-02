# Pending specs

This folder stages each task's pending spec files at their final repo-relative paths until the
task that makes them pass moves them into its real test project (see DECISION-LOG, "Pending specs
are staged in `specs/`"). They reference types that don't exist until their task lands, so placed
in the real test projects they would break the build for every earlier task (C# test projects
compile every `.cs` file in the folder).

**Each file's path under `specs/` is its final repo-relative path.** The task that makes a file
pass moves it into place, removes the `Skip`/`it.skip`, and commits it **in the same commit as the
production code** (see `umb-build-loop-gotchas`).

Specs were written against the names in `ARCHITECTURE.md`/`SPEC.md` without compiling. Each
file's header lists its guesses. Where a builder finds a real signature differs, fix the spec to
the real signature but keep the behavior it asserts and its one-assertion shape.

The first round (T2-T22) is fully moved. Everything below is the M.E.AI-direction rework
(T29-T37, DECISION-LOG 02-10-2026).

## Staged files

Paths below are relative to `specs/`.

| Task | File | Stories / ACs |
|------|------|---------------|
| T32 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Api/Management/Decision/AskDecisionReworkedShapeTests.cs` | DR-4 AC1, AC3, AC11b |
| T33 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/decision/controllers/decision.controller.result-shapes.test.ts` | DR-5 AC1, AC3, AC5 |
| T33 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/decision/repository/decision.server.data-source.state.test.ts` | DR-5 AC5 |
| T35 | `Umbraco.AI.Automate/tests/Umbraco.AI.Automate.Tests.Unit/Actions/DecisionActionsReworkTests.cs` | DR-9 AC1, AC1b, AC3b, AC8 |
| T36 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/property-editors/decision-question-list/property-editor-ui-decision-question-list.element.test.ts` | DR-16 AC1-AC4 |
| T36 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/property-editors/decision-question-list/decision-question-config-modal.element.test.ts` | DR-16 AC7 |
| T37 | `Umbraco.AI.Automate/tests/Umbraco.AI.Automate.Tests.Unit/Actions/AskDecisionsActionTests.cs` | DR-16 AC5, AC6, AC8, AC9 (run-time guard) |

T29's five staged files (`AskTypedDecisionAnswerTests.cs`, `DecisionBatchTests.cs`,
`DecisionTrackingAndChecksHarness.cs`, `DecisionBatchUsageTests.cs`, and
`AIOpenTelemetryDecisionBatchTests.cs`) have moved into their real paths. T30 moved
`ProviderAnswerChecksTests.cs` into place and unskipped it, along with `DecisionBatchTests.cs`'s two
`GivenAProviderThatSkipsAQuestion`/`GivenAProviderThatAnswersAnUnaskedId` cases. T31 moved
`TypeSafeDecisionClientBatchRequestTests.cs` and `TypeSafeDecisionClientGapFillTests.cs` into place
and unskipped them — the real `TypeSafeTestHost.CreateClientAsync`/`GetResponseAsync` signatures
matched the staged assumption exactly, so no spec-side fixes were needed.

## Existing tests each task must update or delete

These assert the old shapes. A task adapts them to the new API, keeping the behavior they pin, or
deletes the cases a staged spec above supersedes.

- **T29 (done):** `FakeDecisionClient`, `AskTypedDecisionTests.cs` (per-kind scenarios superseded by
  `AskTypedDecisionAnswerTests`), `AIDecisionResponseTests.cs` (deleted — the answer types have no
  `Answer`/`Confidence` derivations to pin), `ValidatingDecisionClientTests.cs`,
  `AIDecisionClientFactoryTests.cs`, `AITrackingDecisionClientTests.cs`,
  `AIOpenTelemetryDecisionMiddlewareTests.cs` (trimmed to `Apply_ReturnsWrappedClient`),
  `Services/AIDecisionServiceTests.cs`, `Services/AIDecisionServiceRealPipelineTests.cs`,
  `Providers/CapabilitySettingsRoundTripTests.cs`, `Providers/DeclaredSettingsEnforcementTests.cs`,
  and the Web minimum-compile-fix (`AskDecisionController.cs` + its three test files) all updated to
  `GetResponseAsync`/`AIDecisionRequest`/answer types. `DecisionPipelineHarness.cs`,
  `Api/Management/Common/AICapabilityGateFilterTests.cs` needed no changes (no direct `AskAsync`
  calls).
- **T30 (done):** `Decision/AIDecisionClientFactoryTests.cs` needed no changes — its mismatch tests
  already go through `DecisionAnswerChecker`'s kind check and still assert "recorded as failure".
- **T31 (done):**
  - `TypeSafeDecisionClientRequestTests.cs`: deleted the `"q"`-keyed body lookups and the Context
    cases, superseded by `TypeSafeDecisionClientBatchRequestTests`. Kept the binary, choice and
    score criteria and model-id cases, sent through a one-question request (each question now
    carries an explicit `Id = "q"`, since `AIDecisionRequest` keys by id rather than a fixed `"q"`
    dictionary entry).
  - `TypeSafeDecisionClientResponseTests.cs`: deleted every Level-focused case (`GivenAScoreAnswer`'s
    label-keyed-probabilities assertion, the legend-disagrees-with-levels case, and the three
    clamping cases) — `AIScoreDecisionAnswer` has no `Level` anymore, superseded by
    `TypeSafeDecisionClientGapFillTests` for the gap-fill behavior. Kept
    `GivenAScoreAnswerWithAProbabilityKeyOutsideTheLevels` (ported to the new `Answers["q"]`
    navigation) since it pins a distinct, still-live behavior — dropping an out-of-range
    probability index — not covered by the gap-fill spec. `Probability` became `TrueProbability`.
  - `TypeSafeDecisionClientRetryTests.cs`: calls `GetResponseAsync` with a one-question
    `AIDecisionRequest` instead of `AskAsync`.
  - `TypeSafeProvider.EnsureConnectionValidAsync`'s probe (not a test file, but the one other
    caller of the old single-question `AskAsync`) now builds a one-question `AIDecisionRequest`
    and calls `GetResponseAsync`.
- **T32:** `Api/Management/Decision/AskDecisionControllerTests.cs`:
  - Mock `GetDecisionResponseAsync`.
  - Delete the binary `Answer`/`Confidence` and score `Level` assertions, superseded by
    `AskDecisionReworkedShapeTests`.
  - Levels become `DecisionScoreLevelModel`.
  - `AskDecisionResponseFormattingTests.cs`/`AskDecisionRequestFormattingTests.cs`: update their
    test controllers' payloads.
- **T33:** `decision/controllers/decision.controller.test.ts`, `decision/repository/decision.server.data-source.test.ts`:
  update the binary and score result fixtures to the new shapes.
- **T34 (done):** `Umbraco.AI.Agent/tests/.../Agents/DecisionAgentSelectionTests.cs`:
  - Mock `AskAsync(question, state)`.
  - DR-10 AC2's "Context is the user's message" becomes "state is the user's message".
  - Read `response.Answer.Choice`.
- **T35:** `Umbraco.AI.Automate/tests/.../Actions/DecisionActionsTests.cs`:
  - Mock the new `AskAsync` (with `state`).
  - Delete `AskYesNo_OutputsConfidence`, superseded by `DecisionActionsReworkTests`.
  - Score `Level` is now derived from the question's levels.

## Covered elsewhere, not by a staged spec

- **Wire ACs** (DR-2 AC17, DR-4 AC12, DR-9 AC10, DR-10 AC9, DR-15 AC10, DR-16 AC10) are proven
  by wire tasks T38-T40 and T43, not by specs.
- **DR-16 AC9, flag off at startup** (action missing from the picker): Automate's compose-time
  `Exclude<T>()` has no unit seam, as for the first three actions. Proven by T39. The run-time
  guard is specced in `AskDecisionsActionTests`.
- **DR-14 AC3 for the Guid and builder overloads:** only the alias overload is specced. The
  others share its resolution path, as `AskAsync`'s overloads did.
