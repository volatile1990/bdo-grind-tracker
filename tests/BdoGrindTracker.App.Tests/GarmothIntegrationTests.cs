using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothIntegrationTests
{
    [Fact]
    public void HistoricalDraftUsesStoredClassDurationLootAndCurrentValuation()
    {
        var started = new DateTimeOffset(2026, 9, 6, 18, 0, 0, TimeSpan.FromHours(2));
        var entry = new LootHistoryEntry
        {
            SessionId = Guid.NewGuid(), StartedAt = started, UpdatedAt = started.AddHours(1),
            Duration = TimeSpan.FromHours(1), SpotId = LootSpotCatalog.AphrodonId,
            CharacterClass = "Warrior · Awakening",
            Totals = new Dictionary<string, long> { ["Branch of Abundance"] = 10 },
            SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false
        };

        var draft = MainForm.CreateHistoricalGarmothDraft(entry,
            LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default);

        Assert.Equal(entry.SessionId, draft.LocalSessionId);
        Assert.Equal("Warrior", draft.ClassName);
        Assert.Equal(GarmothSpecialization.Awakening, draft.Specialization);
        Assert.Equal(1_551_270, draft.TotalSilver);
        Assert.Equal(started, draft.StartedAt);
    }

    [Fact]
    public void PayloadUsesVerifiedIdsAndSilverNotItemQuantity()
    {
        var draft = Draft() with { ActiveDuration = TimeSpan.FromSeconds(125), TotalSilver = 100_000_000 };
        var payload = GarmothSessionPayload.Create(draft);
        Assert.Equal(214, payload.GrindspotId);
        Assert.Equal(2, payload.Minutes);
        Assert.Equal(2_880_000_000L, payload.Hourly);
        Assert.Equal(100_000_000, payload.Total);
        Assert.Equal(1582, payload.Drops["980128_0"]);
        Assert.Equal(1, payload.Drops["821431_0"]);
        Assert.Equal(5, payload.ClassId);
        Assert.Equal(1, payload.Spec);
        Assert.False(payload.Global);
        Assert.Contains(draft.LocalSessionId.ToString("N"), payload.Note);
        Assert.Contains("2026-09-05 12:34:56 +02:00", payload.Note);
        Assert.DoesNotContain("Companion", payload.Note);
    }

    [Theory]
    [InlineData("aphrodon", "Branch of Abundance", 213, "980127_0")]
    [InlineData("hermesia", "Black Crystal Fragment", 214, "980128_0")]
    [InlineData("magaia", "Elion Follower's Helmet", 215, "980129_0")]
    [InlineData("aresion", "Scorched Belt Ornament", 216, "980131_0")]
    [InlineData("scales-of-judgment", "Elion Follower's Mark", 217, "980130_0")]
    [InlineData("event-horizon", "Broken Gloves of the Void", 218, "980132_0")]
    public void AllInnerEdaniaSpotTrashMappingsAreExact(string spot, string name, int id, string key)
    {
        var payload = GarmothSessionPayload.Create(Draft() with
        {
            SpotId = spot, Totals = new Dictionary<string, long> { [name] = 42 },
        });
        Assert.Equal(id, payload.GrindspotId);
        Assert.Equal(42, payload.Drops[key]);
    }

    [Fact]
    public void AllSupportedSpotItemsHaveVerifiedMappingExceptExplicitlyUnmappedWorldDrop()
    {
        foreach (var item in LootSpotCatalog.Spots.SelectMany(static spot => spot.AllowedItems).Distinct())
            Assert.Equal(item != "Pure Black Stone", GarmothCatalog.TryGetDropKey(item, out _));
        Assert.False(GarmothCatalog.TryGetDropKey("[Event] Mysterious Ore", out _));
    }

    [Fact]
    public void GlobalPetalIsKeptLocallyButOmittedFromUnsupportedGarmothSpot()
    {
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals) { ["Laila's Petal"] = 3 };
        var payload = GarmothSessionPayload.Create(draft with { Totals = totals });
        Assert.False(payload.Drops.ContainsKey("54031_0"));
        Assert.Equal(["Laila's Petal"], payload.OmittedItems);
        Assert.Equal(["Laila's Petal"], GarmothSessionPayload.GetOmittedItems(draft.SpotId, totals));
        Assert.Empty(GarmothSessionPayload.GetOmittedItems(draft.SpotId, draft.Totals));
        Assert.Equal(3, totals["Laila's Petal"]);
        Assert.Equal(draft.TotalSilver, payload.Total);
    }

    [Theory]
    [InlineData("Pure Black Stone")]
    [InlineData("[Event] Mysterious Ore")]
    [InlineData("Unknown future event item")]
    public void UnmappedDropIsOmittedWithoutBlockingSupportedLoot(string item)
    {
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals) { [item] = 1 };
        var payload = GarmothSessionPayload.Create(draft with { Totals = totals });
        Assert.Equal(1582, payload.Drops["980128_0"]);
        Assert.Equal(1, payload.Drops["821431_0"]);
        Assert.Equal([item], payload.OmittedItems);
        Assert.Equal(1, totals[item]);
    }

    [Fact]
    public void InvalidSpotLootIsNeverAttributedToCurrentSpot()
    {
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals)
        {
            ["Black Gem Fragment"] = 10,
            ["Branch of Abundance"] = 50,
        };
        var payload = GarmothSessionPayload.Create(draft with { Totals = totals });
        Assert.Equal(2, payload.Drops.Count);
        Assert.False(payload.Drops.ContainsKey("980127_0"));
        Assert.Equal(["Black Gem Fragment", "Branch of Abundance"], payload.OmittedItems);
    }

    [Theory]
    [InlineData("aphrodon", 25)]
    [InlineData("hermesia", 27)]
    [InlineData("magaia", 29)]
    [InlineData("aresion", 35)]
    [InlineData("scales-of-judgment", 35)]
    [InlineData("event-horizon", 38)]
    public void UploadSpotListsMatchConfiguredGarmothMetadataWithoutNarrowingLocalPools(string spotId, int count)
    {
        var names = LootSpotCatalog.GetRequired(spotId).AllowedItems;
        Assert.Contains("Laila's Petal", names);
        Assert.Contains("Pure Black Stone", names);
        var totals = names.ToDictionary(static name => name, static _ => 1L);
        var drops = GarmothSessionPayload.GetUploadableDrops(spotId, totals);
        Assert.Equal(count, drops.Count);
        Assert.Equal(["Laila's Petal", "Pure Black Stone"], GarmothSessionPayload.GetOmittedItems(spotId, totals));
    }

    [Theory]
    [InlineData("Pure Black Stone")]
    [InlineData("Laila's Petal")]
    [InlineData("Branch of Abundance")]
    public void SessionWithNoSupportedDropsIsNotUploadedAsEmptySession(string name)
    {
        var draft = Draft() with { Totals = new Dictionary<string, long> { [name] = 1 } };
        Assert.Empty(GarmothSessionPayload.GetUploadableDrops(draft.SpotId, draft.Totals));
        Assert.Equal([name], GarmothSessionPayload.GetOmittedItems(draft.SpotId, draft.Totals));
        var error = Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(draft));
        Assert.Contains("unterstützten Loot", error.Message);
    }

    [Fact]
    public void SelectionResultsAreReadOnlyAndDetachedFromOriginalTotals()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 10, ["Unknown"] = 1 };
        var drops = GarmothSessionPayload.GetUploadableDrops("hermesia", totals);
        var omitted = GarmothSessionPayload.GetOmittedItems("hermesia", totals);
        totals.Clear();
        Assert.Equal(10, drops["980128_0"]);
        Assert.Equal(["Unknown"], omitted);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)drops).Add("unknown", 1));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)omitted).Add("changed"));
    }

    [Fact]
    public void InvalidSpotStillFailsBeforeFilteringItems()
    {
        var draft = Draft() with { SpotId = "unknown" };
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(draft));
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.GetOmittedItems(draft.SpotId, draft.Totals));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveDropTotalsAreRejected(long quantity)
    {
        var draft = Draft() with { Totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = quantity } };
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(draft));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidQuantityIsNotSilentlyHiddenByUnknownItemFiltering(long quantity)
    {
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals) { ["Unknown"] = quantity };
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(draft with { Totals = totals }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(59.999)]
    public void SessionsWithoutOneFullMinuteAreNotUploaded(double seconds) =>
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(
            Draft() with { ActiveDuration = TimeSpan.FromSeconds(seconds) }));

    [Fact]
    public void ZeroSilverMustBeExplicitButIsRepresentable() =>
        Assert.Equal(0, GarmothSessionPayload.Create(Draft() with { TotalSilver = 0 }).Total);

    [Fact]
    public void NegativeAndOverflowingSilverAreRejected()
    {
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(Draft() with { TotalSilver = -1 }));
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(Draft() with
        {
            ActiveDuration = TimeSpan.FromMinutes(1), TotalSilver = long.MaxValue,
        }));
    }

    [Fact]
    public void LargestInt64SilverForAFullHourDoesNotOverflowIntermediateArithmetic()
    {
        var payload = GarmothSessionPayload.Create(Draft() with
        {
            ActiveDuration = TimeSpan.FromHours(1), TotalSilver = long.MaxValue,
        });
        Assert.Equal(long.MaxValue, payload.Hourly);
    }

    [Fact]
    public void PayloadCopiesTotalsAndSerializesNativeFieldTypes()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 10 };
        var payload = GarmothSessionPayload.Create(Draft() with { Totals = totals });
        totals["Black Crystal Fragment"] = 999;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = json.RootElement;
        Assert.Equal(9, root.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("drops").ValueKind);
        Assert.Equal(10, root.GetProperty("drops").GetProperty("980128_0").GetInt64());
        Assert.False(root.GetProperty("global").GetBoolean());
        Assert.Equal(1, root.GetProperty("spec").GetInt32());
        Assert.False(root.TryGetProperty("apiKey", out _));
        Assert.False(root.TryGetProperty("StartedAt", out _));
        Assert.False(root.TryGetProperty("OmittedItems", out _));
    }

    [Fact]
    public void OmittedNamesAreNotAddedToNativeApiContractOrNote()
    {
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals) { ["Unknown future item"] = 2 };
        var payload = GarmothSessionPayload.Create(draft with { Totals = totals });
        Assert.Equal(["Unknown future item"], payload.OmittedItems);
        var json = JsonSerializer.Serialize(payload);
        Assert.DoesNotContain("Unknown future item", json);
        Assert.DoesNotContain("OmittedItems", json);
        Assert.Equal(draft.TotalSilver, payload.Total);
    }

    [Fact]
    public async Task OneClickUploadWithUnknownItemsSendsOnlySupportedDropsOnce()
    {
        var calls = 0;
        string? sentBody = null;
        using var client = new GarmothUploadClient(new FakeHandler(async (request, token) =>
        {
            calls++;
            sentBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }));
        var draft = Draft();
        var totals = new Dictionary<string, long>(draft.Totals)
        {
            ["Pure Black Stone"] = 1,
            ["Laila's Petal"] = 3,
            ["[Event] Mysterious Ore"] = 4,
        };
        draft = draft with { Totals = totals };
        Assert.Equal(GarmothUploadStatus.Succeeded, (await client.UploadAsync(draft, "synthetic-key")).Status);
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted, (await client.UploadAsync(draft, "synthetic-key")).Status);
        Assert.Equal(1, calls);
        using var json = JsonDocument.Parse(sentBody!);
        Assert.Equal(2, json.RootElement.GetProperty("drops").EnumerateObject().Count());
        Assert.Equal(draft.TotalSilver, json.RootElement.GetProperty("total").GetInt64());
        Assert.DoesNotContain("54031_0", sentBody);
        Assert.DoesNotContain("Pure Black Stone", sentBody);
    }

    [Theory]
    [InlineData("Warrior", GarmothSpecialization.Succession, 5, 1)]
    [InlineData("Warrior", GarmothSpecialization.Awakening, 5, 0)]
    [InlineData("Berserker", GarmothSpecialization.Awakening, 0, 0)]
    [InlineData("Archer", GarmothSpecialization.Unique, 16, 0)]
    [InlineData("Shai", GarmothSpecialization.Unique, 17, 1)]
    [InlineData("Scholar", GarmothSpecialization.Unique, 26, 0)]
    [InlineData("Dosa", GarmothSpecialization.Succession, 27, 1)]
    [InlineData("Deadeye", GarmothSpecialization.Unique, 28, 1)]
    [InlineData("Wukong", GarmothSpecialization.Unique, 29, 0)]
    [InlineData("Seraph", GarmothSpecialization.Unique, 30, 1)]
    [InlineData("Agent", GarmothSpecialization.Unique, 31, 1)]
    public void ClassAndSpecializationUseGarmothRatherThanGameIds(string name,
        object specialization, int expectedClass, int expectedSpec)
    {
        Assert.True(GarmothCatalog.TryGetClass(name, (GarmothSpecialization)specialization, out var id, out var spec));
        Assert.Equal(expectedClass, id);
        Assert.Equal(expectedSpec, spec);
    }

    [Theory]
    [InlineData("Unknown", GarmothSpecialization.Succession)]
    [InlineData("Warrior", GarmothSpecialization.Unique)]
    [InlineData("Shai", GarmothSpecialization.Awakening)]
    [InlineData("Archer", GarmothSpecialization.Succession)]
    public void InvalidClassSpecializationsFailValidation(string name, object specialization) =>
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.Create(Draft() with
        {
            ClassName = name, Specialization = (GarmothSpecialization)specialization,
        }));

    [Fact]
    public async Task ExplicitUploadUsesOneHttpsPostWithKeyOnlyInHeader()
    {
        var requests = new List<(HttpMethod Method, Uri? Uri, string Key, string Body)>();
        using var client = new GarmothUploadClient(new FakeHandler(async (request, token) =>
        {
            requests.Add((request.Method, request.RequestUri, request.Headers.GetValues("apiKey").Single(),
                await request.Content!.ReadAsStringAsync(token)));
            return JsonResponse(HttpStatusCode.Created, "{\"id\":123}");
        }));
        Assert.Empty(requests);
        var result = await client.UploadAsync(Draft(), "synthetic-test-api-key");
        Assert.Equal(GarmothUploadStatus.Succeeded, result.Status);
        Assert.True(result.BlocksAnotherUpload);
        var sent = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.garmoth.com/api/external/grind-tracker/sessions/create", sent.Uri!.AbsoluteUri);
        Assert.Equal("synthetic-test-api-key", sent.Key);
        Assert.DoesNotContain(sent.Key, sent.Body);
        Assert.DoesNotContain(sent.Key, result.ToString());
    }

    [Fact]
    public async Task SuccessfulSessionCannotBeUploadedAgainEvenWhenDraftChanges()
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"success\":true}"));
        }));
        var draft = Draft();
        Assert.Equal(GarmothUploadStatus.Succeeded, (await client.UploadAsync(draft, "test-key")).Status);
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted,
            (await client.UploadAsync(draft with { TotalSilver = 1 }, "new-key")).Status);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(422)]
    [InlineData(429)]
    public async Task DefiniteRejectionAllowsOnlyAnExplicitRetryAndDoesNotEchoBody(int status)
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse((HttpStatusCode)status, "{\"error\":\"secret-key\"}"));
        }));
        var draft = Draft();
        var result = await client.UploadAsync(draft, "secret-key");
        Assert.Equal(GarmothUploadStatus.Rejected, result.Status);
        Assert.False(result.BlocksAnotherUpload);
        Assert.DoesNotContain("secret-key", result.Message);
        Assert.Equal(1, calls);
        await client.UploadAsync(draft, "secret-key");
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(409)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task AmbiguousResponseBlocksDuplicateUpload(int status)
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Location = new Uri("https://untrusted.invalid/");
            return Task.FromResult(response);
        }));
        var draft = Draft();
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, (await client.UploadAsync(draft, "key")).Status);
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted, (await client.UploadAsync(draft, "key")).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TimeoutDoesNotLeakExceptionOrRetry()
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            throw new TaskCanceledException("private-secret-key");
        }));
        var draft = Draft();
        var result = await client.UploadAsync(draft, "private-secret-key");
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, result.Status);
        Assert.DoesNotContain("private-secret-key", result.Message);
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted, (await client.UploadAsync(draft, "key")).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationBeforeDispatchDoesNotConsumeSessionOrSendAnything()
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var draft = Draft();
        Assert.Equal(GarmothUploadStatus.Rejected,
            (await client.UploadAsync(draft, "key", new CancellationToken(true))).Status);
        Assert.Equal(0, calls);
        Assert.Equal(GarmothUploadStatus.Succeeded, (await client.UploadAsync(draft, "key")).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key\r\nInjected: yes")]
    [InlineData("key with space")]
    [InlineData("not-ascii-ä")]
    public async Task InvalidKeyCannotCauseARequest(string key)
    {
        using var client = new GarmothUploadClient(new FakeHandler((_, _) => throw new Exception("No send allowed")));
        Assert.Equal(GarmothUploadStatus.Rejected, (await client.UploadAsync(Draft(), key)).Status);
    }

    [Theory]
    [InlineData("text/html", "<html>Login or challenge</html>")]
    [InlineData("application/json", "{malformed}")]
    public async Task SuspiciousSuccessBodyIsUnknownNotSilentlySuccessful(string mediaType, string body)
    {
        using var client = new GarmothUploadClient(new FakeHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) })));
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, (await client.UploadAsync(Draft(), "key")).Status);
    }

    [Fact]
    public async Task OversizedResponseIsNotReadOrRetried()
    {
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, new string('x', 70_000)))));
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, (await client.UploadAsync(Draft(), "key")).Status);
    }

    [Fact]
    public async Task ResponseBodyIsSubjectToWholeRequestDeadline()
    {
        var body = new DelayedContent();
        using var client = new GarmothUploadClient(new FakeHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = body })), TimeSpan.FromMilliseconds(50));
        var draft = Draft();
        var result = await client.UploadAsync(draft, "test-key").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(body.Started);
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, result.Status);
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted, (await client.UploadAsync(draft, "key")).Status);
    }

    [Fact]
    public async Task UnknownLengthResponseCannotExceedBufferLimit()
    {
        using var client = new GarmothUploadClient(new FakeHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnboundedLengthContent() })));
        Assert.Equal(GarmothUploadStatus.OutcomeUnknown, (await client.UploadAsync(Draft(), "key")).Status);
    }

    [Theory]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"status\":false}")]
    [InlineData("{\"error\":\"private-key-should-not-leak\"}")]
    public async Task ExplicitApplicationRejectionIsNotReportedAsSuccess(string body)
    {
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, body))));
        var result = await client.UploadAsync(Draft(), "private-key-should-not-leak");
        Assert.Equal(GarmothUploadStatus.Rejected, result.Status);
        Assert.DoesNotContain("private-key-should-not-leak", result.Message);
    }

    [Fact]
    public async Task DistinctLocalSessionsCanEachBeUploadedExactlyOnce()
    {
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }));
        var draft = Draft();
        Assert.Equal(GarmothUploadStatus.Succeeded, (await client.UploadAsync(draft, "key")).Status);
        Assert.Equal(GarmothUploadStatus.Succeeded,
            (await client.UploadAsync(draft with { LocalSessionId = Guid.NewGuid() }, "key")).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcurrentUploadOfSameSessionSendsOnlyOnce()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new GarmothUploadClient(new FakeHandler((_, _) => { calls++; return pending.Task; }));
        var draft = Draft();
        var first = client.UploadAsync(draft, "key");
        var second = await client.UploadAsync(draft, "key");
        Assert.Equal(GarmothUploadStatus.AlreadySubmitted, second.Status);
        Assert.Equal(1, calls);
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.Equal(GarmothUploadStatus.Succeeded, (await first).Status);
    }

    private static GarmothSessionDraft Draft() => new(Guid.NewGuid(), "hermesia", "Warrior",
        GarmothSpecialization.Succession, TimeSpan.FromMinutes(30),
        new Dictionary<string, long> { ["Black Crystal Fragment"] = 1582, ["BON Origin Shard"] = 1 },
        250_000_000, new DateTimeOffset(2026, 9, 5, 12, 34, 56, TimeSpan.FromHours(2)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            handler(request, token);
    }

    private sealed class DelayedContent : HttpContent
    {
        public bool Started { get; private set; }
        public DelayedContent() => Headers.ContentType = new("application/json");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The cancellation-aware body overload is required.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
        {
            Started = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class UnboundedLengthContent : HttpContent
    {
        public UnboundedLengthContent() => Headers.ContentType = new("application/json");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[70_000]).AsTask();
    }
}
