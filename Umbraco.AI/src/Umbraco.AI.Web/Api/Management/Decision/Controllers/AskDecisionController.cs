using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Decision;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Core.Providers.Errors;
using Umbraco.AI.Core.Settings;
using Umbraco.AI.Extensions;
using Umbraco.AI.Web.Api.Common.Models;
using Umbraco.AI.Web.Api.Management.Decision.Models;

#pragma warning disable UMBRACOAI_DECISION // Consumes the experimental Decision capability service

namespace Umbraco.AI.Web.Api.Management.Decision.Controllers;

/// <summary>
/// Controller to ask a Decision question against a Decision profile.
/// </summary>
/// <remarks>
/// Wire shape mapping is a minimum compile fix against the reworked Core Decision contract (question's
/// <c>Context</c> moved to the request's shared <c>State</c>; responses are now typed answers keyed by
/// question id) — it maps onto the existing <see cref="DecisionResponseModel"/> wire shapes as closely
/// as possible rather than reworking them. The full wire rework (dropping the request's per-question
/// <c>context</c> in favour of a top-level <c>state</c>, flat per-kind response shapes) is a later task.
/// </remarks>
[ApiVersion("1.0")]
public class AskDecisionController : DecisionControllerBase
{
    private readonly IAIDecisionService _decisionService;
    private readonly IAIProfileService _profileService;
    private readonly IAIExperimentalFeatures _experimentalFeatures;

    /// <summary>
    /// Initializes a new instance of the <see cref="AskDecisionController"/> class.
    /// </summary>
    public AskDecisionController(
        IAIDecisionService decisionService,
        IAIProfileService profileService,
        IAIExperimentalFeatures experimentalFeatures)
    {
        _decisionService = decisionService;
        _profileService = profileService;
        _experimentalFeatures = experimentalFeatures;
    }

    /// <summary>
    /// Ask a binary, choice, or score Decision question.
    /// </summary>
    /// <param name="model">The Decision request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed Decision response, matching the question's <c>$type</c>.</returns>
    [HttpPost("ask")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(DecisionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ask(
        [FromBody] AskDecisionRequestModel model,
        CancellationToken cancellationToken = default)
    {
        // Belt-and-suspenders alongside the shared AICapabilityGateFilter (applied via
        // DecisionControllerBase's [AICapabilityGate(AICapability.Decision)]): that filter protects
        // real HTTP traffic (it runs before body model binding, which a polymorphic $type can
        // otherwise fail), but a caller invoking this action directly (as unit tests do) bypasses the
        // MVC filter pipeline entirely, so the flag is also checked here.
        if (!_experimentalFeatures.IsCapabilityEnabled(AICapability.Decision))
        {
            return NotFound();
        }

        try
        {
            return model.Question switch
            {
                BinaryDecisionQuestionModel binary
                    => await AskAsync(MapQuestion(binary), binary.Context, model.ProfileIdOrAlias, cancellationToken),
                ChoiceDecisionQuestionModel choice
                    => await AskAsync(MapQuestion(choice), choice.Context, model.ProfileIdOrAlias, cancellationToken),
                ScoreDecisionQuestionModel score
                    => await AskScoreAsync(MapQuestion(score), score, model.ProfileIdOrAlias, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported decision question type '{model.Question.GetType().Name}'.")
            };
        }
        catch (InvalidOperationException ex)
        {
            // A supplied-but-missing profile is already handled above via TryGetProfileIdAsync before
            // the service is ever called, so any InvalidOperationException reaching here is a
            // configuration problem (no default Decision profile, or a resolved profile that isn't a
            // Decision profile) rather than a "not found" the caller can fix by retrying — see SPEC.md's
            // guarantees for POST decision/ask.
            return BadRequest(new ProblemDetails
            {
                Title = "Decision failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid question",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (AIProviderException ex)
        {
            // Covers both a provider validation failure (e.g. Jev 422, AIProviderErrorCategory.InvalidRequest)
            // and other classified provider failures (auth, rate limit, overloaded, network) — all surfaced
            // the same way GenerateImageController surfaces its own provider-level failures.
            return BadRequest(new ProblemDetails
            {
                Title = "Decision request failed",
                Detail = ex.UserMessage,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    // Validates the already-mapped Core question via the shared Umbraco.AI.Core.Decision.
    // DecisionQuestionValidator (the same rules ValidatingDecisionClient enforces for any C# caller),
    // then resolves the profile and calls the service. Validation deliberately runs first — before
    // profile resolution and any provider call — see ARCHITECTURE.md's Security section and SPEC.md's
    // guarantees for POST decision/ask. The ArgumentException catch below remains as defence in depth
    // in case the mapped question still reaches ValidatingDecisionClient with something this doesn't
    // parse for.
    private async Task<IActionResult> AskAsync(
        AIBinaryDecisionQuestion question,
        string? state,
        string? profileIdOrAlias,
        CancellationToken cancellationToken)
    {
        var validationError = DecisionQuestionValidator.ValidateQuestion(question);
        if (validationError is not null)
        {
            return BadRequest(InvalidQuestion(validationError));
        }

        var profileId = await TryResolveProfileAsync(profileIdOrAlias, cancellationToken);
        if (profileId.IsFailure)
        {
            return profileId.FailureResult!;
        }

        AIDecisionResponse<AIBinaryDecisionAnswer> response = await _decisionService.AskAsync(
            b => Configure(b, profileId.ProfileId), question, state, cancellationToken: cancellationToken);

        return Ok(MapResponse(response.Answer, response.ModelId, response.Usage));
    }

    private async Task<IActionResult> AskAsync(
        AIChoiceDecisionQuestion question,
        string? state,
        string? profileIdOrAlias,
        CancellationToken cancellationToken)
    {
        var validationError = DecisionQuestionValidator.ValidateQuestion(question);
        if (validationError is not null)
        {
            return BadRequest(InvalidQuestion(validationError));
        }

        var profileId = await TryResolveProfileAsync(profileIdOrAlias, cancellationToken);
        if (profileId.IsFailure)
        {
            return profileId.FailureResult!;
        }

        AIDecisionResponse<AIChoiceDecisionAnswer> response = await _decisionService.AskAsync(
            b => Configure(b, profileId.ProfileId), question, state, cancellationToken: cancellationToken);

        return Ok(MapResponse(response.Answer, response.ModelId, response.Usage));
    }

    private async Task<IActionResult> AskScoreAsync(
        AIScoreDecisionQuestion question,
        ScoreDecisionQuestionModel model,
        string? profileIdOrAlias,
        CancellationToken cancellationToken)
    {
        var validationError = DecisionQuestionValidator.ValidateQuestion(question);
        if (validationError is not null)
        {
            return BadRequest(InvalidQuestion(validationError));
        }

        var profileId = await TryResolveProfileAsync(profileIdOrAlias, cancellationToken);
        if (profileId.IsFailure)
        {
            return profileId.FailureResult!;
        }

        AIDecisionResponse<AIScoreDecisionAnswer> response = await _decisionService.AskAsync(
            b => Configure(b, profileId.ProfileId), question, model.Context, cancellationToken: cancellationToken);

        return Ok(MapResponse(response.Answer, model.Levels, response.ModelId, response.Usage));
    }

    private IActionResult Ok(DecisionResponseModel mapped) =>
        // Ok(mapped) would lose the `$type` discriminator: OkObjectResult's constructor takes
        // `object? value`, so the compile-time type DecisionResponseModel is erased and MVC's output
        // formatter falls back to the *runtime* type (e.g. BinaryDecisionResponseModel) — a type with
        // no [JsonDerivedType] attributes of its own, so System.Text.Json never emits "$type". Setting
        // DeclaredType explicitly (the same field ActionResult<T>.Convert() sets from typeof(TValue))
        // tells the formatter to serialize against the base contract instead, which is where
        // [JsonPolymorphic]/[JsonDerivedType] are declared. See SPEC.md's guarantees for POST decision/ask.
        new OkObjectResult(mapped) { DeclaredType = typeof(DecisionResponseModel) };

    private async Task<ProfileResolution> TryResolveProfileAsync(string? profileIdOrAlias, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileIdOrAlias))
        {
            return ProfileResolution.Success(null);
        }

        var profileId = await _profileService.TryGetProfileIdAsync(IdOrAlias.Parse(profileIdOrAlias, null), cancellationToken);
        return profileId.HasValue
            ? ProfileResolution.Success(profileId)
            : ProfileResolution.Failure(ProfileNotFound());
    }

    private static void Configure(AIDecisionBuilder b, Guid? profileId)
    {
        b.WithAlias("management-api-decision");
        if (profileId.HasValue)
        {
            b.WithProfile(profileId.Value);
        }
    }

    private readonly struct ProfileResolution
    {
        private ProfileResolution(Guid? profileId, IActionResult? failureResult)
        {
            ProfileId = profileId;
            FailureResult = failureResult;
        }

        public Guid? ProfileId { get; }

        public IActionResult? FailureResult { get; }

        public bool IsFailure => FailureResult is not null;

        public static ProfileResolution Success(Guid? profileId) => new(profileId, null);

        public static ProfileResolution Failure(IActionResult result) => new(null, result);
    }

    private static ProblemDetails InvalidQuestion(string detail) => new()
    {
        Title = "Invalid question",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };

    private static AIBinaryDecisionQuestion MapQuestion(BinaryDecisionQuestionModel model) => new()
    {
        Instructions = model.Instructions,
        TrueCriteria = model.TrueCriteria,
        FalseCriteria = model.FalseCriteria
    };

    private static AIChoiceDecisionQuestion MapQuestion(ChoiceDecisionQuestionModel model) => new()
    {
        Instructions = model.Instructions,
        // Options (and its entries) may still be null here despite the Web model's `required` — that
        // keyword only enforces the JSON property's presence, not a non-null value — so null is passed
        // through rather than dereferenced, letting DecisionQuestionValidator reject it uniformly.
        Options = model.Options?.Select(o => o is null ? null! : new AIDecisionOption(o.Key, o.Description)).ToList()!
    };

    private static AIScoreDecisionQuestion MapQuestion(ScoreDecisionQuestionModel model) => new()
    {
        Instructions = model.Instructions,
        // Levels may still be null here for the same reason Options may be — see above.
        Levels = model.Levels?.Select(l => new AIDecisionScoreLevel(l)).ToList()!
    };

    private static BinaryDecisionResponseModel MapResponse(AIBinaryDecisionAnswer answer, string? modelId, UsageDetails? usage)
    {
        var isTrue = answer.IsTrue();
        return new BinaryDecisionResponseModel
        {
            Answer = isTrue,
            Probability = answer.TrueProbability,
            Confidence = isTrue ? answer.TrueProbability : 1 - answer.TrueProbability,
            ModelId = modelId,
            Usage = MapUsage(usage)
        };
    }

    private static ChoiceDecisionResponseModel MapResponse(AIChoiceDecisionAnswer answer, string? modelId, UsageDetails? usage) => new()
    {
        Choice = answer.Choice,
        Confidence = answer.Confidence ?? 0,
        Probabilities = answer.Probabilities,
        ModelId = modelId,
        Usage = MapUsage(usage)
    };

    private static ScoreDecisionResponseModel MapResponse(
        AIScoreDecisionAnswer answer, IReadOnlyList<string>? levelLabels, string? modelId, UsageDetails? usage)
    {
        levelLabels ??= [];
        var nearestIndex = Math.Clamp((int)Math.Round(answer.Score), 0, Math.Max(levelLabels.Count - 1, 0));
        var level = levelLabels.Count > nearestIndex ? levelLabels[nearestIndex] : string.Empty;

        var probabilitiesByLabel = new Dictionary<string, double>();
        foreach (var (index, probability) in answer.Probabilities)
        {
            if (index >= 0 && index < levelLabels.Count)
            {
                probabilitiesByLabel[levelLabels[index]] = probability;
            }
        }

        return new ScoreDecisionResponseModel
        {
            Score = answer.Score,
            Level = level,
            Confidence = answer.Confidence ?? 0,
            Probabilities = probabilitiesByLabel,
            ModelId = modelId,
            Usage = MapUsage(usage)
        };
    }

    private static UsageModel? MapUsage(UsageDetails? usage) => usage is null
        ? null
        : new UsageModel
        {
            InputTokens = usage.InputTokenCount,
            OutputTokens = usage.OutputTokenCount,
            TotalTokens = usage.TotalTokenCount
        };
}
