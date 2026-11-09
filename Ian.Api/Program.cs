using System.Diagnostics;
using Ian.Api;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

// Keep request activities when the hosting logger is disabled to omit raw request paths.
using var requestActivities = new ActivityListener
{
    ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
    SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.PropagationData
};
ActivitySource.AddActivityListener(requestActivities);

var app = ApiHost.Build(WebApplication.CreateBuilder(args));
app.Run();
