using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Tests;

[TestClass]
public sealed class YnabApiClientTests
{
    [TestMethod]
    public async Task GetCategoriesAsync_SendsBearerTokenAndForcesFullSync()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"category_groups\":[],\"server_knowledge\":42}}");
        var client = CreateClient(handler);

        var result = await client.GetCategoriesAsync();

        Assert.AreEqual(42, result.Data.ServerKnowledge);
        Assert.AreEqual(HttpMethod.Get, handler.Request!.Method);
        Assert.AreEqual(
            "/v1/plans/plan-1/categories?last_knowledge_of_server=0",
            handler.Request.RequestUri!.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.AreEqual("test-key", handler.Request.Headers.Authorization.Parameter);
    }

    [TestMethod]
    public async Task GetTransactionsAsync_SendsAllConfiguredQueryParameters()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"transactions\":[],\"server_knowledge\":7}}");
        var client = CreateClient(handler);

        await client.GetTransactionsAsync(new GetTransactionsOptions
        {
            SinceDate = new DateOnly(2025, 1, 2),
            UntilDate = new DateOnly(2025, 1, 5),
            Type = TransactionType.Unapproved,
            LastKnowledgeOfServer = 6
        });

        Assert.AreEqual(
            "/v1/plans/plan-1/transactions?since_date=2025-01-02&until_date=2025-01-05&type=unapproved&last_knowledge_of_server=6",
            handler.Request!.RequestUri!.PathAndQuery);
    }

    [TestMethod]
    public async Task UpdateTransactionsAsync_SendsOnlyIdentifiersAndCategoryId()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"transaction_ids\":[\"transaction-1\"]}}");
        var client = CreateClient(handler);

        await client.UpdateTransactionsAsync(
        [
            UpdateTransactionsRequest.ById("transaction-1", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            UpdateTransactionsRequest.ByImportId("import-1", null)
        ]);

        using var body = JsonDocument.Parse(handler.Body!);
        var transactions = body.RootElement.GetProperty("transactions");
        Assert.AreEqual("transaction-1", transactions[0].GetProperty("id").GetString());
        Assert.AreEqual(
            "11111111-1111-1111-1111-111111111111",
            transactions[0].GetProperty("category_id").GetString());
        Assert.AreEqual("import-1", transactions[1].GetProperty("import_id").GetString());
        Assert.IsFalse(transactions[1].TryGetProperty("id", out _));
        Assert.AreEqual(HttpMethod.Patch, handler.Request!.Method);
    }

    [TestMethod]
    public async Task ApiError_PreservesYnabErrorDetails()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"id\":\"400\",\"name\":\"BadRequest\",\"detail\":\"Invalid category\"}}");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsExceptionAsync<YnabApiException>(() => client.GetPlansAsync());

        Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.AreEqual("Invalid category", exception.Error!.Detail);
    }

    [TestMethod]
    public async Task AddYnabApiAsync_ResolvesTheOnlyPlanDuringRegistration()
    {
        var recordingHandler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"plans\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Only plan\"}]}}");
        var configuration = CreateConfiguration();
        var discoveryOptions = Options.Create(new YnabOptions { ApiKey = "test-key" });
        var authenticationHandler = new YnabAuthenticationHandler(discoveryOptions)
        {
            InnerHandler = recordingHandler
        };
        using var discoveryClient = new HttpClient(authenticationHandler)
        {
            BaseAddress = new Uri("https://api.ynab.com/v1/")
        };

        var services = new ServiceCollection();
        await services.AddYnabApi(configuration, discoveryClient);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<YnabOptions>>().Value;

        Assert.AreEqual("11111111-1111-1111-1111-111111111111", options.PlanId);
        Assert.AreEqual("/v1/plans", recordingHandler.Request!.RequestUri!.PathAndQuery);
        Assert.AreEqual("test-key", recordingHandler.Request.Headers.Authorization!.Parameter);
    }

    [TestMethod]
    public async Task AddYnabApiAsync_RejectsMultiplePlansDuringRegistration()
    {
        var recordingHandler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"plans\":[{\"id\":\"11111111-1111-1111-1111-111111111111\"},{\"id\":\"22222222-2222-2222-2222-222222222222\"}]}}");
        var configuration = CreateConfiguration();
        using var discoveryClient = new HttpClient(recordingHandler)
        {
            BaseAddress = new Uri("https://api.ynab.com/v1/")
        };
        var services = new ServiceCollection();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            services.AddYnabApi(configuration, discoveryClient));
    }

    private static IYnabApiClient CreateClient(RecordingHandler handler)
    {
        var options = Options.Create(new YnabOptions
        {
            ApiKey = "test-key",
            PlanId = "plan-1"
        });

        var authenticationHandler = new YnabAuthenticationHandler(options)
        {
            InnerHandler = handler
        };
        var httpClient = new HttpClient(authenticationHandler)
        {
            BaseAddress = new Uri("https://api.ynab.com/v1/")
        };

        return new YnabApiClient(httpClient, options);
    }

    private static IConfiguration CreateConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration["ynab_api_key"] = "test-key";
        return configuration;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
