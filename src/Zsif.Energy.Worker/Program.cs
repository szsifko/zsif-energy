using Microsoft.Extensions.Options;
using Zsif.Energy.Worker;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSystemd();

builder.Services.AddOptions<SmartmeterOptions>()
    .Bind(builder.Configuration.GetSection(SmartmeterOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient(SmartmeterClient.HttpClientName, (serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<SmartmeterOptions>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "de-AT");
    })
    .AddStandardResilienceHandler(resilience =>
    {
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
    });

builder.Services.AddOptions<MqttOptions>()
    .Bind(builder.Configuration.GetSection(MqttOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuthenticationService>();
builder.Services.AddSingleton<SmartmeterClient>();
builder.Services.AddSingleton<MeterCalculationService>();
builder.Services.AddSingleton<MqttPublisher>();

//Publisher registered first so it stops last and can still report offline
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MqttPublisher>());
builder.Services.AddHostedService<SmartmeterWorker>();

var host = builder.Build();
host.Run();
