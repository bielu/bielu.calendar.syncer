using Bielu.Calendar.Syncer;
using Bielu.Calendar.Syncer.Dashboard;
using Bielu.Calendar.Syncer.Google;
using Bielu.Calendar.Syncer.Microsoft;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services
    .AddCalendarSyncer(options => builder.Configuration.GetSection(CalendarSyncerOptions.SectionName).Bind(options))
    .AddGoogleCalendar(options => builder.Configuration.GetSection(GoogleCalendarOptions.SectionName).Bind(options))
    .AddMicrosoftCalendar(options => builder.Configuration.GetSection(MicrosoftCalendarOptions.SectionName).Bind(options));

var app = builder.Build();

app.MapCalendarSyncerDashboard();

await app.RunAsync();
