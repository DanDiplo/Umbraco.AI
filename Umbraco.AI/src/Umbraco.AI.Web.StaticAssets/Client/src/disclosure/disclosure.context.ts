/**
 * Disclosure Context
 *
 * Global context that tells AI surfaces (chat, prompt previews) whether to show the
 * "Responses are AI-generated" notice, based on the global AI setting, and remembers a
 * per-browser dismissal when the setting allows it.
 *
 * Auto-provided at the backoffice root via the globalContext manifest.
 *
 * @example
 * ```typescript
 * import { UAI_DISCLOSURE_CONTEXT } from '@umbraco-ai/core';
 *
 * this.consumeContext(UAI_DISCLOSURE_CONTEXT, (context) => {
 *   this.observe(context?.showNotice, (show) => (this._showNotice = show ?? false));
 *   this.observe(context?.canDismiss, (canDismiss) => (this._canDismiss = canDismiss ?? false));
 * });
 * ```
 */

import { UmbControllerBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import {
    UmbBasicState,
    UmbBooleanState,
    createObservablePart,
    mergeObservables,
} from "@umbraco-cms/backoffice/observable-api";
import { SettingsService } from "../api/sdk.gen.js";
import { coreClientReady } from "../client-ready.js";
import type { UaiDisclosureNoticeMode } from "./types.js";

const DISMISSED_COOKIE = "umbAIDisclosureNoticeDismissed";
const DISMISSED_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

function isKnownMode(value: unknown): value is UaiDisclosureNoticeMode {
    return value === "Always" || value === "Dismissible" || value === "Off";
}

function readDismissedCookie(): boolean {
    try {
        return document.cookie.split("; ").some((cookie) => cookie === `${DISMISSED_COOKIE}=1`);
    } catch {
        return false;
    }
}

function writeDismissedCookie(): void {
    try {
        const secure = location.protocol === "https:" ? "; Secure" : "";
        document.cookie = `${DISMISSED_COOKIE}=1; Max-Age=${DISMISSED_MAX_AGE_SECONDS}; Path=/; SameSite=Strict${secure}`;
    } catch {
        // Cookies blocked: the dismissal still holds for this page load via state.
    }
}

/**
 * Global context providing the AI disclosure notice state.
 * Registered as a globalContext manifest - auto-instantiated at backoffice root.
 */
export class UaiDisclosureContext extends UmbControllerBase {
    /** Type guard marker for context resolution. */
    public readonly IS_DISCLOSURE_CONTEXT = true;

    // Undefined until loaded, so nothing flashes up and then disappears when the mode is Off.
    readonly #mode = new UmbBasicState<UaiDisclosureNoticeMode | undefined>(undefined);
    readonly #dismissed = new UmbBooleanState(readDismissedCookie());

    /** The configured notice mode, or undefined while loading. */
    readonly mode = this.#mode.asObservable();

    /** Whether the notice should be shown right now. */
    readonly showNotice = mergeObservables(
        [this.#mode.asObservable(), this.#dismissed.asObservable()],
        ([mode, dismissed]) => mode === "Always" || (mode === "Dismissible" && !dismissed),
    );

    /** Whether the notice can be dismissed by the user. */
    readonly canDismiss = createObservablePart(this.#mode.asObservable(), (mode) => mode === "Dismissible");

    constructor(host: UmbControllerHost) {
        super(host);
        this.provideContext(UAI_DISCLOSURE_CONTEXT, this);
        void this.#load();
    }

    async #load(): Promise<void> {
        await coreClientReady;
        try {
            const { data } = await SettingsService.getDisclosureSettings();
            // Anything unexpected falls back to Always: a failure must never hide the notice.
            this.#mode.setValue(isKnownMode(data?.noticeMode) ? data.noticeMode : "Always");
        } catch {
            this.#mode.setValue("Always");
        }
    }

    /**
     * Updates the mode without a reload, e.g. straight after the AI settings are saved.
     */
    setMode(mode: UaiDisclosureNoticeMode): void {
        this.#mode.setValue(isKnownMode(mode) ? mode : "Always");
    }

    /**
     * Hides the notice for this browser. Only has an effect when the mode is Dismissible.
     */
    dismiss(): void {
        if (this.#mode.getValue() !== "Dismissible") return;
        writeDismissedCookie();
        this.#dismissed.setValue(true);
    }
}

export default UaiDisclosureContext;

export const UAI_DISCLOSURE_CONTEXT = new UmbContextToken<UaiDisclosureContext>("UaiDisclosureContext");
