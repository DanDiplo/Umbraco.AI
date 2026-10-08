// S3 — Compatible contract, persisted and exposed (AC3.1, AC3.4).
using System.Text.Json;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Tests;

namespace Umbraco.AI.Tests.Unit.Tests;

public class AITestTokenUsageSerializationTests
{
    private static readonly JsonSerializerOptions Options = Umbraco.AI.Core.Constants.DefaultJsonSerializerOptions;

    public class GivenTokenUsageWithTwoModelEntries
    {
        private readonly AITestTokenUsage _original;
        private readonly AITestTokenUsage _loaded;

        public GivenTokenUsageWithTwoModelEntries()
        {
            _original = new AITestTokenUsage
            {
                InputTokens = 130,
                OutputTokens = 45,
                TotalTokens = 175,
                CallCount = 3,
                UnreportedCallCount = 1,
                Models =
                [
                    new AITestModelTokenUsage
                    {
                        Capability = AICapability.Chat,
                        ProviderId = "openai",
                        ModelId = "gpt-x",
                        ProfileId = Guid.NewGuid(),
                        ProfileAlias = "p1",
                        InputTokens = 100,
                        OutputTokens = 20,
                        TotalTokens = 120,
                        CallCount = 2,
                        UnreportedCallCount = 0
                    },
                    new AITestModelTokenUsage
                    {
                        Capability = AICapability.Embedding,
                        ProviderId = "openai",
                        ModelId = "embed-x",
                        ProfileId = Guid.NewGuid(),
                        ProfileAlias = "p2",
                        InputTokens = 30,
                        OutputTokens = 25,
                        TotalTokens = 55,
                        CallCount = 1,
                        UnreportedCallCount = 1
                    }
                ]
            };

            var json = JsonSerializer.Serialize(_original, Options);
            _loaded = JsonSerializer.Deserialize<AITestTokenUsage>(json, Options)!;
        }

        [Fact]
        public void KeepsTheTotalTokens()
        {
            _loaded.TotalTokens.ShouldBe(_original.TotalTokens);
        }

        [Fact]
        public void KeepsTheCallCount()
        {
            _loaded.CallCount.ShouldBe(_original.CallCount);
        }

        [Fact]
        public void KeepsBothModelEntries()
        {
            _loaded.Models.Count.ShouldBe(2);
        }

        [Fact]
        public void KeepsTheModelIdentity()
        {
            _loaded.Models[0].ShouldBeEquivalentTo(_original.Models[0]);
        }
    }

    public class GivenJsonWrittenBeforeThisChange
    {
        private readonly AITestTokenUsage _loaded;

        public GivenJsonWrittenBeforeThisChange()
        {
            _loaded = JsonSerializer.Deserialize<AITestTokenUsage>(
                """{"inputTokens":10,"outputTokens":5,"totalTokens":15}""", Options)!;
        }

        [Fact]
        public void LoadsWithAnEmptyModelsList()
        {
            _loaded.Models.ShouldBeEmpty();
        }

        [Fact]
        public void LoadsWithCallCountZero()
        {
            _loaded.CallCount.ShouldBe(0);
        }
    }
}
