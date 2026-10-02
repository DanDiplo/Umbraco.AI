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
| T29 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Decision/AskTypedDecisionAnswerTests.cs` | DR-1 AC1, AC2, AC3, AC4, AC4b, AC4c, AC13 |
| T29 (+T30) | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Decision/DecisionBatchTests.cs` | DR-14 AC1, AC3, AC6, AC7, AC8; AC9, AC10 are `Pending T30` |
| T29 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Decision/DecisionTrackingAndChecksHarness.cs` | shared arrange (real factory + tracker, audit/usage mocked) for the next two files |
| T29 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Decision/DecisionBatchUsageTests.cs` | DR-14 AC2 |
| T29 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Middleware/AIOpenTelemetryDecisionBatchTests.cs` | DR-14 AC4 |
| T30 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Decision/ProviderAnswerChecksTests.cs` | DR-15 AC1-AC9 |
| T31 | `Umbraco.AI.TypeSafe/tests/Umbraco.AI.TypeSafe.Tests.Unit/TypeSafeDecisionClientBatchRequestTests.cs` | DR-2 AC2, AC6, AC7; DR-14 AC5 |
| T31 | `Umbraco.AI.TypeSafe/tests/Umbraco.AI.TypeSafe.Tests.Unit/TypeSafeDecisionClientGapFillTests.cs` | DR-2 AC9, AC9b |
| T32 | `Umbraco.AI/tests/Umbraco.AI.Tests.Unit/Api/Management/Decision/AskDecisionReworkedShapeTests.cs` | DR-4 AC1, AC3, AC11b |
| T33 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/decision/controllers/decision.controller.result-shapes.test.ts` | DR-5 AC1, AC3, AC5 |
| T33 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/decision/repository/decision.server.data-source.state.test.ts` | DR-5 AC5 |
| T35 | `Umbraco.AI.Automate/tests/Umbraco.AI.Automate.Tests.Unit/Actions/DecisionActionsReworkTests.cs` | DR-9 AC1, AC1b, AC3b, AC8 |
| T36 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/property-editors/decision-question-list/property-editor-ui-decision-question-list.element.test.ts` | DR-16 AC1-AC4 |
| T36 | `Umbraco.AI/src/Umbraco.AI.Web.StaticAssets/Client/src/property-editors/decision-question-list/decision-question-config-modal.element.test.ts` | DR-16 AC7 |
| T37 | `Umbraco.AI.Automate/tests/Umbraco.AI.Automate.Tests.Unit/Actions/AskDecisionsActionTests.cs` | DR-16 AC5, AC6, AC8, AC9 (run-time guard) |

## Existing tests each task must update or delete

These assert the old shapes. A task adapts them to the new API, keeping the behavior they pin, or
deletes the cases a staged spec above supersedes.

- **T29:**
  - `Umbraco.AI/tests/Umbraco.AI.Tests.Common/Fakes/FakeDecisionClient.cs`: switch to
    `Func<AIDecisionRequest, AIDecisionResponse>`, record `(Request, Options)`, implement
    `GetResponseAsync`. The staged specs assume this.
  - `Decision/AskTypedDecisionTests.cs`: delete the per-kind answer scenarios (superseded by
    `AskTypedDecisionAnswerTests`). Adapt the profile alias/default/config-alias/no-default and
    invalid-question scenarios to `AskAsync(question, state)`.
  - `Decision/AIDecisionResponseTests.cs`: delete or rewrite against the answer types. The
    old `Answer`/`Confidence` derivations are gone.
  - `Decision/ValidatingDecisionClientTests.cs`, `Decision/AIDecisionClientFactoryTests.cs`,
    `Decision/DecisionPipelineHarness.cs`, `Services/AIDecisionServiceTests.cs`,
    `Services/AIDecisionServiceRealPipelineTests.cs`: change to `GetResponseAsync` and requests.
    The factory's mismatch tests return a wrong-kind answer.
  - `Middleware/AITrackingDecisionClientTests.cs`: update the prompt and audit snapshot
    expectations to "Tracking and telemetry" (the question list with ids and `State`; the answer
    per id).
  - `Middleware/AIOpenTelemetryDecisionMiddlewareTests.cs`: delete the per-kind
    `gen_ai.request.kind` tests, superseded by `AIOpenTelemetryDecisionBatchTests`. Keep
    `Apply_ReturnsWrappedClient`.
  - `Providers/CapabilitySettingsRoundTripTests.cs`, `Providers/DeclaredSettingsEnforcementTests.cs`,
    `Api/Management/Common/AICapabilityGateFilterTests.cs`: compile fixes for the new client
    contract.
- **T30:** `Decision/AIDecisionClientFactoryTests.cs`: the mismatch tests now go through the
  answer checks. Keep the "recorded as failure" assertions.
- **T31:**
  - `TypeSafeDecisionClientRequestTests.cs`: delete the `"q"`-keyed body lookups and the Context
    cases, superseded by `TypeSafeDecisionClientBatchRequestTests`. Keep the binary, choice and
    score criteria and model-id cases, sent through a one-question request.
  - `TypeSafeDecisionClientResponseTests.cs`: delete the label-keyed score probabilities,
    superseded by `TypeSafeDecisionClientGapFillTests`. `Probability` becomes `TrueProbability`.
  - `TypeSafeDecisionClientRetryTests.cs`: call `GetResponseAsync`.
- **T32:** `Api/Management/Decision/AskDecisionControllerTests.cs`:
  - Mock `GetDecisionResponseAsync`.
  - Delete the binary `Answer`/`Confidence` and score `Level` assertions, superseded by
    `AskDecisionReworkedShapeTests`.
  - Levels become `DecisionScoreLevelModel`.
  - `AskDecisionResponseFormattingTests.cs`/`AskDecisionRequestFormattingTests.cs`: update their
    test controllers' payloads.
- **T33:** `decision/controllers/decision.controller.test.ts`, `decision/repository/decision.server.data-source.test.ts`:
  update the binary and score result fixtures to the new shapes.
- **T34:** `Umbraco.AI.Agent/tests/.../Agents/DecisionAgentSelectionTests.cs`:
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
