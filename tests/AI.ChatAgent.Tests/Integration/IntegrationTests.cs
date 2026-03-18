using AI.ChatAgent.Data;
using AI.ChatAgent.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AI.ChatAgent.Tests.Integration;

/// <summary>
/// Integration tests using WebApplicationFactory.
/// Full HTTP pipeline: routing, middleware, services, DB.
/// AI completion calls are not made (no real API key in CI).
/// </summary>
public sealed class ApiIntegrationTests : IClassFixture<TestWebAppFactory>
{
	private readonly HttpClient _client;

	public ApiIntegrationTests(TestWebAppFactory factory)
	{
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task GET_Health_Returns200()
	{
		var response = await _client.GetAsync(AppConstants.HealthEndpoint);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task POST_Chat_InvalidContentType_Returns415()
	{
		var response = await _client.PostAsync(
			AppConstants.ChatEndpoint,
			new StringContent("hello", System.Text.Encoding.UTF8, "text/plain"));
		response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
	}

	[Fact]
	public async Task POST_Chat_EmptyMessage_Returns400()
	{
		var request = new ChatRequest { Message = "" };
		var response = await _client.PostAsJsonAsync(AppConstants.ChatEndpoint, request);
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task POST_Chat_MessageTooLong_Returns400()
	{
		var request = new ChatRequest { Message = new string('x', 33_000) };
		var response = await _client.PostAsJsonAsync(AppConstants.ChatEndpoint, request);
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task GET_Approvals_Returns200()
	{
		var response = await _client.GetAsync("/approvals");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task POST_Approvals_NonExistentId_Returns404()
	{
		var response = await _client.PostAsync(
			"/approvals/nonexistent-id/resolve?approved=true", null);
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GET_ChatHistory_Returns200()
	{
		var response = await _client.GetAsync("/chat/unknown-session-id/history");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var body = await response.Content.ReadAsStringAsync();
		body.Should().Contain("messages");
	}
}

/// <summary>Custom WebApplicationFactory — replaces DB with in-memory and stubs AI keys.</summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
	protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
	{
		builder.ConfigureServices(services =>
		{
			// ── Remove every descriptor whose ServiceType or ImplementationType
			//    comes from an EF Core assembly. This is the only reliable way to
			//    clear the SQLite provider registrations before adding InMemory.
			//    Removing only DbContextOptions<T> leaves internal provider services
			//    behind, causing "two database providers registered" at runtime.
			var efAssemblyPrefix = "Microsoft.EntityFrameworkCore";

			var toRemove = services
				.Where(d =>
				{
					var st = d.ServiceType.Assembly.FullName ?? "";
					var it = d.ImplementationType?.Assembly.FullName ?? "";
					return st.StartsWith(efAssemblyPrefix, StringComparison.Ordinal)
						|| it.StartsWith(efAssemblyPrefix, StringComparison.Ordinal);
				})
				.ToList();

			foreach (var d in toRemove)
				services.Remove(d);

			// Re-add DbContext with a fresh, isolated in-memory database.
			services.AddDbContext<ChatAgentDbContext>(opt =>
				opt.UseInMemoryDatabase($"IntegrationTest-{Guid.NewGuid()}"));

			// Stub AI keys — kernel must construct without real credentials in CI.
			services.Configure<AI.ChatAgent.Configuration.AiOptions>(opt =>
			{
				opt.Provider = "OpenAI";
				opt.OpenAI.ApiKey = "sk-test-integration-stub";
				opt.OpenAI.ChatModelId = "gpt-4o-mini";
			});
		});

		builder.UseEnvironment("Testing");
	}
}
