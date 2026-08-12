using System.Text.Json;
using Ingest.Api.Controllers;
using Ingest.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

public sealed class BootstrapControllerTests
{
    [Theory]
    [InlineData(" en-us ", "en-US")]
    [InlineData(" ", "en-US")]
    [InlineData("not_a_locale", "en-US")]
    public void Get_returns_normalized_safe_default_locale(string configured, string expected)
    {
        var controller = new BootstrapController(Options.Create(new IngestOptions
        {
            DefaultLocale = configured,
        }));

        var result = Assert.IsType<OkObjectResult>(controller.Get());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));

        Assert.Equal(expected, json.RootElement.GetProperty("defaultLocale").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }
}
