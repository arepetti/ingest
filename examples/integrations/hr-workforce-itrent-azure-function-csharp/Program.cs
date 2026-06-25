// Isolated-worker host bootstrap for the Azure Functions app. The single function
// lives in WeeklyWorkforceFunction.cs and runs on a weekly timer trigger.
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Build().Run();
