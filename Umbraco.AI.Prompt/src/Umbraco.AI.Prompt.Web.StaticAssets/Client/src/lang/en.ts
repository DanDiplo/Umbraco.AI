import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

export default {
    uaiPrompt: {
        deleteConfirm: "Are you sure you want to delete this prompt?",
        bulkDeleteConfirm: (count: number) => `Are you sure you want to delete ${count} prompt(s)?`,
        aiGeneratedNotice: "Responses are AI-generated and may be inaccurate.",
    },
    tiptapGroup : {
        ai: "AI",
    }
} as UmbLocalizationDictionary;
