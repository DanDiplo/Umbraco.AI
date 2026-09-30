import type { UaiChatMessage, UaiToolCallInfo } from "@umbraco-ai/agent-ui";
import type { MessageResponseModel } from "../api/types.gen.js";

/** Shown for a restored tool call that never got a result (e.g. the run was stopped mid-call). */
export const UAI_RESTORED_RESULT_UNAVAILABLE = "Result unavailable (conversation was restored).";

/**
 * The parts of a persisted M.E.AI `ChatMessage` content item (`contentJson` is the serialized
 * `ChatMessage`, discriminated by `$type`) that the display needs.
 */
interface StoredContent {
    $type?: string;
    text?: string;
    // functionCall
    callId?: string;
    name?: string;
    arguments?: unknown;
    // functionResult
    result?: unknown;
    // toolApprovalRequest / toolApprovalResponse
    toolCall?: { callId?: string; name?: string; arguments?: unknown };
    approved?: boolean;
    // error
    message?: string;
    errorCode?: string;
}

/**
 * Maps persisted messages into the chat UI's `UaiChatMessage` shape for **display only**. This seeds
 * the thread when a conversation opens; it does NOT feed the model — on each turn the server supplies
 * the authoritative history from its durable store (the client transmits only the new turn).
 *
 * The result mirrors what a live run leaves behind, so a reopened conversation looks the same as it
 * did while it ran and tool renderers (including custom tool views) get the same inputs:
 * - assistant messages split the way the live run controller splits them: tool calls keep joining the
 *   current message (so a turn's chips sit together), and text that arrives after tool calls starts
 *   a new message. Stored rows are split per model call, so they're regrouped here rather than shown
 *   one row per message;
 * - each tool call's `arguments` / `result` are the JSON serialisation of the stored values, which is
 *   exactly what the live AG-UI stream sends (`AGUIEventEmitter` serialises the same objects);
 * - a hidden tool-role message after the assistant message for every tool call. The run controller
 *   repairs any tool call without one by appending a synthesized result, which would both show a
 *   bogus "unavailable" chip and shift the strategy's persisted-message count — so every call gets one.
 *
 * Rows that can't be read as a stored `ChatMessage` fall back to their plain text.
 */
export function toDisplayMessages(messages: readonly MessageResponseModel[]): UaiChatMessage[] {
    const display: UaiChatMessage[] = [];
    const deniedCallIds = new Set<string>();
    // Every call in the current user turn, by id — an approved call is stored twice (the approval
    // request, then the call itself) and a result can arrive in a later row than its call.
    let turnCalls = new Map<string, UaiToolCallInfo>();
    let segment: { message: UaiChatMessage; calls: Map<string, UaiToolCallInfo> } | undefined;

    const flushSegment = () => {
        if (!segment) return;
        const { message, calls } = segment;
        segment = undefined;

        const toolCalls = [...calls.values()].map((call) => {
            if (call.result === undefined) {
                return { ...call, status: "error" as const, result: UAI_RESTORED_RESULT_UNAVAILABLE };
            }
            return deniedCallIds.has(call.id) ? { ...call, status: "error" as const } : call;
        });

        if (!message.content.trim() && toolCalls.length === 0) return;

        display.push(toolCalls.length > 0 ? { ...message, toolCalls } : message);
        for (const call of toolCalls) {
            display.push({
                id: `${message.id}:${call.id}`,
                role: "tool",
                content: call.result ?? "",
                toolCallId: call.id,
                timestamp: message.timestamp,
            });
        }
    };

    for (const stored of messages) {
        const timestamp = new Date(stored.dateCreated);
        const contents = readContents(stored.contentJson);

        if (stored.role === "user") {
            for (const content of contents ?? []) {
                if (content.$type === "toolApprovalResponse" && content.approved === false && content.toolCall?.callId) {
                    deniedCallIds.add(content.toolCall.callId);
                }
            }

            // An approval answer is stored as a user row with no text; it belongs to the turn it answers.
            const text = contents ? joinText(contents) : (stored.contentText ?? "");
            if (!text.trim()) continue;

            flushSegment();
            turnCalls = new Map();
            display.push({ id: stored.id, role: "user", content: text, timestamp });
            continue;
        }

        if (stored.role !== "assistant" && stored.role !== "tool") continue;

        /** The message new content goes into. Text after tool calls starts a new one, as it does live. */
        const current = (forText = false) => {
            if (forText && segment && segment.calls.size > 0) flushSegment();
            segment ??= {
                message: { id: stored.id, role: "assistant", content: "", timestamp },
                calls: new Map(),
            };
            return segment;
        };

        if (!contents) {
            if (stored.role === "assistant" && stored.contentText) {
                appendText(current(true).message, stored.contentText);
            }
            continue;
        }

        for (const content of contents) {
            switch (content.$type) {
                case "text":
                    if (stored.role === "assistant" && content.text) appendText(current(true).message, content.text);
                    break;
                case "functionCall":
                    addCall(turnCalls, current, content.callId, content.name, content.arguments);
                    break;
                case "toolApprovalRequest":
                    addCall(turnCalls, current, content.toolCall?.callId, content.toolCall?.name, content.toolCall?.arguments);
                    break;
                case "functionResult": {
                    const call = content.callId ? turnCalls.get(content.callId) : undefined;
                    if (call) {
                        call.result = JSON.stringify(content.result ?? null);
                        call.status = "completed";
                    }
                    break;
                }
                case "error":
                    appendText(current(true).message, formatProviderError(content));
                    break;
            }
        }
    }

    flushSegment();
    return display;
}

function readContents(contentJson: string): StoredContent[] | undefined {
    try {
        const parsed = JSON.parse(contentJson) as { contents?: unknown };
        return Array.isArray(parsed?.contents) ? (parsed.contents as StoredContent[]) : undefined;
    } catch {
        return undefined;
    }
}

function joinText(contents: readonly StoredContent[]): string {
    return contents
        .filter((c) => c.$type === "text" && c.text)
        .map((c) => c.text)
        .join("");
}

function appendText(message: UaiChatMessage, text: string): void {
    if (!text) return;
    message.content = message.content ? `${message.content}${text}` : text;
}

function addCall(
    turnCalls: Map<string, UaiToolCallInfo>,
    current: () => { calls: Map<string, UaiToolCallInfo> },
    callId?: string,
    name?: string,
    args?: unknown,
): void {
    // An approved call is stored twice (the approval request, then the call itself) — show it once.
    if (!callId || !name || turnCalls.has(callId)) return;
    const call: UaiToolCallInfo = {
        id: callId,
        name,
        arguments: JSON.stringify(args ?? {}),
        status: "pending",
    };
    turnCalls.set(callId, call);
    current().calls.set(callId, call);
}

/** Matches the inline text the live stream emits for provider errors (AGUIStreamingService). */
function formatProviderError(content: StoredContent): string {
    const message = content.message || "(no message)";
    return content.errorCode
        ? `\n\n[Provider error ${content.errorCode}: ${message}]\n\n`
        : `\n\n[Provider error: ${message}]\n\n`;
}
