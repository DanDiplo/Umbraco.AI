// DR-16 — Ask several questions in one Automate step (AC7): the config modal refuses a bad alias.
//
// ASSUMPTIONS (T36 builder confirms/adjusts, keeping each test's behavior and single assertion):
// - Element `uai-decision-question-config-modal` (UmbModalBaseElement), file
//   decision-question-config-modal.element.ts beside the list editor. Its `data` is
//   { kind, existingQuestion?, otherAliases: string[] }; its submit button has id "btn-submit".
// - The alias input has id "alias" and the instructions input id "instructions"; a refused submit
//   renders the reason in an element with class "alias-error".
// - Submitting calls `modalContext.submit()`; tests stub modalContext to observe that.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "./decision-question-config-modal.element.js";
import type { UaiDecisionQuestionConfigModalElement } from "./decision-question-config-modal.element.js";

beforeAll(() => {
    if (!HTMLElement.prototype.attachInternals) {
        HTMLElement.prototype.attachInternals = function () {
            return { setValidity: vi.fn(), form: null } as unknown as ElementInternals;
        };
    }
});

async function renderWith(alias: string, otherAliases: string[] = []) {
    const el = document.createElement("uai-decision-question-config-modal") as UaiDecisionQuestionConfigModalElement;
    const submit = vi.fn();
    el.data = { kind: "binary", otherAliases } as never;
    el.modalContext = { submit, reject: vi.fn(), setValue: vi.fn(), getValue: vi.fn() } as never;
    document.body.appendChild(el);
    await el.updateComplete;

    const set = (id: string, value: string) => {
        const input = el.shadowRoot!.getElementById(id) as HTMLElement & { value?: string };
        input.value = value;
        input.dispatchEvent(new Event("input"));
    };
    set("alias", alias);
    set("instructions", "Is it?");
    await el.updateComplete;
    el.shadowRoot!.getElementById("btn-submit")!.dispatchEvent(new MouseEvent("click"));
    await el.updateComplete;
    return { el, submit };
}

describe("Feature: decision question config modal", () => {
    afterEach(() => {
        document.body.innerHTML = "";
    });

    describe("Scenario: a valid alias", () => {
        let submit: ReturnType<typeof vi.fn>;

        beforeEach(async () => {
            ({ submit } = await renderWith("refund_1"));
        });

        // Pending T36
        it.skip("submits", () => {
            expect(submit).toHaveBeenCalledOnce();
        });
    });

    describe("Scenario: an invalid alias", () => {
        for (const [why, alias, others] of [
            ["is blank", "", []],
            ["starts with a digit", "1refund", []],
            ["has a space", "re fund", []],
            ["duplicates another question's", "refund", ["refund"]],
        ] as const) {
            describe(`when it ${why}`, () => {
                let el: UaiDecisionQuestionConfigModalElement;
                let submit: ReturnType<typeof vi.fn>;

                beforeEach(async () => {
                    ({ el, submit } = await renderWith(alias, [...others]));
                });

                // Pending T36
                it.skip("doesn't submit", () => {
                    expect(submit).not.toHaveBeenCalled();
                });

                // Pending T36
                it.skip("shows why", () => {
                    expect(el.shadowRoot!.querySelector(".alias-error")).not.toBeNull();
                });
            });
        }
    });
});
