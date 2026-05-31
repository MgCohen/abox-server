using Microsoft.Extensions.Logging;
using RemoteAgents.UI.Components.Api;

namespace RemoteAgents.UI.Maui;

public static class MauiProgram
{
	// v1: HostBaseAddress is compiled-in. Override by editing this constant
	// before building for your phone — needs to be the Tailscale IPv4 of
	// the laptop running RemoteAgents.Host. For development on the same
	// machine, http://localhost:5062/ matches the dev launch profile.
	//
	// Future: load from a MAUI Preferences key set at first run, or a
	// settings page inside the app.
	public const string DefaultHostBaseAddress = "http://localhost:5062/";

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Single HttpClient + typed HostApiClient — same shape UI.Web uses.
		builder.Services.AddScoped(_ => new HttpClient
		{
			BaseAddress = new Uri(DefaultHostBaseAddress),
		});
		builder.Services.AddScoped<HostApiClient>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
