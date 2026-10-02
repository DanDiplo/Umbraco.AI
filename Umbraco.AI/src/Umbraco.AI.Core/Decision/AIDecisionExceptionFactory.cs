using Umbraco.AI.Core.Providers.Errors;

#pragma warning disable UMBRACOAI_DECISION // Implements the experimental decision capability surface

namespace Umbraco.AI.Core.Decision;

/// <summary>
/// Builds the <see cref="AIProviderException"/>s thrown when a provider's answer doesn't match what a
/// question asked for.
/// </summary>
/// <remarks>
/// Used by <see cref="AIErrorClassifyingDecisionClient"/> — made inside the tracking middleware so the
/// failure is recorded (see its remarks and the <c>provider-contract-checks-inside-tracking</c> memory
/// entry) — and by <see cref="AIDecisionService"/>'s narrowing guard afterwards, as defence in depth.
/// Centralising the construction here keeps the message/category from drifting between the two.
/// </remarks>
internal static class AIDecisionExceptionFactory
{
    internal static AIProviderException CreateAnswerTypeMismatchException(AIDecisionQuestion question, AIDecisionAnswer actualAnswer) =>
        new(new AIProviderErrorInfo(
            AIProviderErrorCategory.Unknown,
            $"The AI provider answered question '{question.Id}' with a '{actualAnswer.GetType().Name}', but it expected a '{question.ExpectedAnswerType.Name}'.",
            ProviderCode: null,
            RawMessage: $"Question '{question.Id}' expected answer type '{question.ExpectedAnswerType.FullName}' but received '{actualAnswer.GetType().FullName}'."));

    internal static AIProviderException CreateAnswerTypeMismatchException(Type expectedAnswerType, AIDecisionAnswer actualAnswer) =>
        new(new AIProviderErrorInfo(
            AIProviderErrorCategory.Unknown,
            $"The AI provider returned a '{actualAnswer.GetType().Name}' answer, but this question expected a '{expectedAnswerType.Name}'.",
            ProviderCode: null,
            RawMessage: $"Expected answer type '{expectedAnswerType.FullName}' but received '{actualAnswer.GetType().FullName}'."));

    internal static AIProviderException CreateMissingAnswerException(AIDecisionQuestion question) =>
        new(new AIProviderErrorInfo(
            AIProviderErrorCategory.Unknown,
            $"The AI provider did not answer question '{question.Id}'.",
            ProviderCode: null,
            RawMessage: $"No answer was returned for question id '{question.Id}'."));
}
