using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Middleware.Application.Dtos.Requests;
using Middleware.Infrastructure.Sap;
using Middleware.IntegrationTests.Infrastructure;
using Polly.Timeout;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// POST /stock-transfers/based-on-document must never create a second StockTransfer for the same
/// idempotency key, and must return the document SAP created even when the POST itself failed
/// (timeout / connection cut / SAP rejecting the retry because the base document is already consumed).
/// Incident 2026-09-10: 5 receptions ended as job ERROR "SAP saturado" although SAP had created them.
/// </summary>
public sealed class StockTransferIdempotencyTests : IAsyncLifetime
{
    private const string Endpoint = "/api/v1/stock-transfers/based-on-document";
    private const string Key = "reception-transfer-77";

    private readonly ScriptedSapFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static bool IsLookup(ScriptedSapServiceLayerClient.Call c, string key)
        => c.Method == HttpMethod.Get && c.Url.StartsWith("StockTransfers?$filter=U_sap_id eq '", StringComparison.Ordinal)
           && c.Url.Contains($"'{key}'", StringComparison.Ordinal);

    private static bool IsCreate(ScriptedSapServiceLayerClient.Call c)
        => c.Method == HttpMethod.Post && c.Url == "StockTransfers";

    // Same warehouses and base line as Request() below — what SAP returns for OUR earlier attempt.
    private static string Found(int docEntry, int docNum) => $$"""
        {"value":[{"DocEntry":{{docEntry}},"DocNum":{{docNum}},"FromWarehouse":"TOUT-LLG","ToWarehouse":"OUT-LLG",
                   "StockTransferLines":[{"ItemCode":"UWM-2731.L","Quantity":40.0,"BaseEntry":845922,"BaseLine":0}]}]}
        """;

    // A transfer of ANOTHER request that happens to carry the same key (another module reused it).
    private static string FoundOtherRequest(int docEntry, int docNum) => $$"""
        {"value":[{"DocEntry":{{docEntry}},"DocNum":{{docNum}},"FromWarehouse":"TRL-PSP","ToWarehouse":"RL-PSP",
                   "StockTransferLines":[{"ItemCode":"OTRO-ITEM","Quantity":1.0,"BaseEntry":845935,"BaseLine":0}]}]}
        """;
    private const string NotFound = """{"value":[]}""";

    private static CreateStockTransferBasedOnDocumentRequest Request(string? uSapId = null, bool extraScannedLine = false) => new(
        DocObjectCode: "67",
        CardCode: null,
        DocDate: "2026-09-10",
        TaxDate: null,
        Comments: "OUT-LLG - prueba",
        FromWarehouse: "TOUT-LLG",
        ToWarehouse: "OUT-LLG",
        U_ORIGEN: "C",
        Series: null,
        PriceList: null,
        StockTransferLines: extraScannedLine
            ?
            [
                new StockTransferBasedOnDocumentLineDto(67, 845922, 0, "UWM-2731.L", 40m, "OUT-LLG"),
                new StockTransferBasedOnDocumentLineDto(67, 845922, 1, "UWM-2731.M", 40m, "OUT-LLG")
            ]
            : [new StockTransferBasedOnDocumentLineDto(67, 845922, 0, "UWM-2731.L", 40m, "OUT-LLG")],
        U_sap_id: uSapId);

    private async Task<HttpResponseMessage> PostAsync(CreateStockTransferBasedOnDocumentRequest request, string? idempotencyKey)
    {
        var client = _factory.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(request) };
        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(message);
    }

    [Fact]
    public async Task IdempotencyKeyHeader_IsCheckedFirst_AndWrittenAsUSapId()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, Key) => NotFound,
            _ when IsCreate(call) => """{"DocEntry":10,"DocNum":500}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(10);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse("it was created in this call");

        var calls = _factory.SapClient.Calls;
        calls.Should().HaveCount(2, "one lookup by U_sap_id, then one create");
        IsLookup(calls[0], Key).Should().BeTrue();
        IsCreate(calls[1]).Should().BeTrue();

        using var payload = JsonDocument.Parse(calls[1].Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(Key,
            "the key must be persisted in SAP so a later attempt can find the document");
    }

    [Fact]
    public async Task ExistingDocumentForKey_IsReturned_WithoutCreatingAnother()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, Key) => Found(7, 300),
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(7);
        body.GetProperty("docNum").GetInt32().Should().Be(300);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue("the module gets the same 201, but the flag tells it was not created now");

        _factory.SapClient.Posts.Should().BeEmpty("a retry must not create a second StockTransfer");
    }

    [Fact]
    public async Task SameBaseDocument_WithLinesScannedAfterTheFirstAttempt_ReturnsExistingDocument()
    {
        // Reception omits zero-quantity lines, so a retry after more scanning carries MORE lines than
        // the document SAP already created. Same base document + warehouses → it is our transfer.
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, Key) => Found(7, 300),
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(extraScannedLine: true), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "SAP cannot amend the existing document; a 409 here would strand the reception");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(7);
        _factory.SapClient.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task KeyAlreadyUsedByAnotherRequest_Returns409_AndCreatesNothing()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, Key) => FoundOtherRequest(99, 900),
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "returning another module's transfer, or creating a second one with the same key, would both be wrong");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain(Key).And.Contain("DocNum=900");

        _factory.SapClient.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task PostTimesOut_ButSapCreatedTheDocument_ReturnsThatDocument()
    {
        var lookups = 0;
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, Key))
            {
                // Not there before the POST; there after SAP committed it despite our timeout.
                return Task.FromResult(++lookups == 1 ? NotFound : Found(8, 400));
            }

            if (IsCreate(call))
            {
                throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'.");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "SAP did create the transfer — the caller must get it, not an error");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(8);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();

        _factory.SapClient.Posts.Should().HaveCount(1);
        _factory.SapClient.Gets.Count(c => IsLookup(c, Key)).Should().Be(2, "lookup before the POST and again after the timeout");
    }

    [Fact]
    public async Task SapRejectsRetry_BecauseEarlierAttemptAlreadyCreatedIt_ReturnsExistingDocument()
    {
        var lookups = 0;
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, Key))
            {
                return Task.FromResult(++lookups == 1 ? NotFound : Found(9, 401));
            }

            if (IsCreate(call))
            {
                throw new SapException(HttpStatusCode.BadRequest,
                    """{"error":{"code":-5002,"message":{"lang":"en-us","value":"Quantity cannot exceed the open quantity of the base document"}}}""");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task SapRejects_AndNoDocumentExistsForKey_PropagatesSapError()
    {
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, Key))
            {
                return Task.FromResult(NotFound);
            }

            if (IsCreate(call))
            {
                throw new SapException(HttpStatusCode.BadRequest,
                    """{"error":{"code":-5002,"message":{"lang":"en-us","value":"Target item number does not match base item number"}}}""");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await PostAsync(Request(), Key);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "a genuine SAP rejection must still surface as an error");
        _factory.SapClient.Posts.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyLongerThanTheSapField_Returns400_WithoutCallingSap(bool inBody)
    {
        var tooLong = new string('K', 61); // OWTR.U_sap_id is alphanumeric(60)
        _factory.SapClient.Handler = call => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");

        var response = inBody
            ? await PostAsync(Request(tooLong), idempotencyKey: null)
            : await PostAsync(Request(), tooLong);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("61").And.Contain("60");
        _factory.SapClient.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task KeyWithForbiddenCharacters_Returns400_WithoutCallingSap()
    {
        _factory.SapClient.Handler = call => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");

        var response = await PostAsync(Request("clave con espacios 'y' comillas"), idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.SapClient.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task BodyUSapId_TakesPrecedenceOverHeader()
    {
        const string bodyKey = "BODY-KEY-1";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, bodyKey) => NotFound,
            _ when IsCreate(call) => """{"DocEntry":11,"DocNum":501}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(bodyKey), "header-key-ignored");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(bodyKey);
    }

    [Fact]
    public async Task NoKeyProvided_GeneratesAGuidCorrelationId()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            { Method.Method: "GET" } => NotFound,
            _ when IsCreate(call) => """{"DocEntry":12,"DocNum":502}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await PostAsync(Request(), idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        var generated = payload.RootElement.GetProperty("U_sap_id").GetString();
        Guid.TryParse(generated, out _).Should().BeTrue($"'{generated}' should be a GUID");
        generated.Should().Be(generated!.ToUpperInvariant(), "same format api-transferencias-directas already uses against the UDF");
    }
}
