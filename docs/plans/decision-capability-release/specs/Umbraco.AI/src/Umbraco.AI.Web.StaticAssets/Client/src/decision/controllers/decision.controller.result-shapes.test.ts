// DR-5 — Ask decisions from TypeScript (AC1, AC3, AC5): the reworked result shapes and state option.
//
// ASSUMPTIONS (T33 builder confirms/adjusts, keeping each test's behavior and single assertion):
// - UaiBinaryDecisionResult is { kind: "binary", trueProbability, modelId?, usage? } (no answer/confidence).
// - UaiScoreDecisionResult.probabilities is Record<number, number>, keyed by level index; no `level`.
// - ask(question, { state?, profileIdOrAlias?, signal? }) forwards `state` to the repository request.
// Supersedes the binary/score result expectations in decision.controller.test.ts.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { UmbElementControllerHost } from "@umbraco-cms/backoffice/controller-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";

// Stub at the real boundary: the repository the public controller delegates to.
const ask = vi.fn();
vi.mock("../repository/decision.repository.js", () => ({
    UaiDecisionRepository: class {
        ask = ask;
    },
}));

import { UaiDecisionController } from "./decision.controller.js";
import type { UaiBinaryDecisionResult, UaiScoreDecisionResult } from "../types.js";

function createHost(): UmbControllerHost {
    const element = document.createElement("div");
    document.body.appendChild(element);
    const host = new UmbElementControllerHost(element);
    host.hostConnected();
    return host;
}

describe("Feature: UaiDecisionController result shapes", () => {
    describe("Scenario: a binary question is answered", () => {
        let result: { data?: UaiBinaryDecisionResult; error?: unknown };

        beforeEach(async () => {
            ask.mockReset();
            ask.mockResolvedValue({ data: { kind: "binary", trueProbability: 0.97, modelId: "jev-1.13.0" } });
            result = await new UaiDecisionController(createHost()).ask({ kind: "binary", instructions: "Is this spam?" });
        });

        // Pending T33
        it.skip("returns the true probability", () => {
            expect(result.data?.trueProbability).toBe(0.97);
        });

        // Pending T33
        it.skip("has no answer field", () => {
            expect(result.data).not.toHaveProperty("answer");
        });

        // Pending T33
        it.skip("has no confidence field", () => {
            expect(result.data).not.toHaveProperty("confidence");
        });
    });

    describe("Scenario: a score question is answered", () => {
        let result: { data?: UaiScoreDecisionResult; error?: unknown };

        beforeEach(async () => {
            ask.mockReset();
            ask.mockResolvedValue({
                data: { kind: "score", score: 1.8, confidence: 0.8, probabilities: { 0: 0.05, 1: 0.15, 2: 0.8 } },
            });
            result = await new UaiDecisionController(createHost()).ask({
                kind: "score",
                instructions: "How good?",
                levels: [{ description: "poor" }, { description: "ok" }, { description: "good" }],
            });
        });

        // Pending T33
        it.skip("keys probabilities by level index", () => {
            expect(result.data?.probabilities[2]).toBe(0.8);
        });
    });

    describe("Scenario: state and a profile are passed as options", () => {
        beforeEach(async () => {
            ask.mockReset();
            ask.mockResolvedValue({ data: { kind: "binary", trueProbability: 0.5 } });
            await new UaiDecisionController(createHost()).ask(
                { kind: "binary", instructions: "Is this spam?" },
                { state: "Buy cheap watches", profileIdOrAlias: "spam-check" },
            );
        });

        // Pending T33
        it.skip("forwards the state to the repository", () => {
            expect(ask.mock.calls[0][0].state).toBe("Buy cheap watches");
        });

        // Pending T33
        it.skip("forwards the profile to the repository", () => {
            expect(ask.mock.calls[0][0].profileIdOrAlias).toBe("spam-check");
        });
    });
});
