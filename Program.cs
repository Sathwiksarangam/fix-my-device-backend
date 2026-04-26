using System.Text.Json;
DeviceSystemInfoRequest? latestDevice = null;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFlutterApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFlutterApp");

app.MapGet("/", () => "Fix My Device API is running");

app.MapGet("/api/devices", () =>
{
    if (latestDevice == null)
    {
        return Results.Ok(Array.Empty<object>());
    }

    return Results.Ok(new[]
    {
        new
        {
            id = "device-real-001",
            deviceName = latestDevice.DeviceName,
            systemType = latestDevice.SystemType,
            status = "Online",
            lastSeenAt = "Just now",
            processor = latestDevice.Processor,
            installedRam = latestDevice.Ram,
            graphicsCard = latestDevice.Graphics,
            totalStorage = latestDevice.Storage,
            freeStorage = latestDevice.FreeStorage,
            windowsVersion = latestDevice.WindowsVersion,
            drives = latestDevice.Drives
        }
    });
});

app.MapPost("/api/devices/system-info", (DeviceSystemInfoRequest request) =>
{
    latestDevice = request;

    Console.WriteLine("Received and saved system info:");
    Console.WriteLine(JsonSerializer.Serialize(request, new JsonSerializerOptions
    {
        WriteIndented = true
    }));

    return Results.Ok(new
    {
        message = "System info saved successfully",
        receivedAt = DateTime.Now.ToString("g"),
        device = new
        {
            id = "device-real-001",
            deviceName = request.DeviceName,
            processor = request.Processor,
            installedRam = request.Ram,
            graphicsCard = request.Graphics,
            totalStorage = request.Storage,
            freeStorage = request.FreeStorage,
            systemType = request.SystemType,
            windowsVersion = request.WindowsVersion,
            status = "Online",
            lastSeenAt = "Just now",
            drives = request.Drives
        }
    });
});

app.Run();

record DeviceSystemInfoRequest(
    string DeviceName,
    string Processor,
    string Ram,
    string Graphics,
    string Storage,
    string FreeStorage,
    string SystemType,
    string WindowsVersion,
    List<DriveInfoRequest> Drives
);

record DriveInfoRequest(
    string DriveLetter,
    string DriveType,
    string TotalSize,
    string FreeSpace
);