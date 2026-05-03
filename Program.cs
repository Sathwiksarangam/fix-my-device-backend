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
            processor = latestDevice.ProcessorName,
            processorSpeed = latestDevice.ProcessorSpeed,
            installedRam = latestDevice.InstalledRam,
            usableRam = latestDevice.UsableRam,
            graphicsCard = latestDevice.GraphicsCard,
            graphicsMemory = latestDevice.GraphicsMemory,
            totalStorage = latestDevice.TotalStorage,
            usedStorage = latestDevice.UsedStorage,
            freeStorage = latestDevice.FreeStorage,
            deviceId = latestDevice.DeviceId,
            productId = latestDevice.ProductId,
            systemType = latestDevice.SystemType,
            windowsEdition = latestDevice.WindowsEdition,
            windowsVersion = latestDevice.WindowsVersion,
            osBuild = latestDevice.OsBuild,
            installedOn = latestDevice.InstalledOn,
            status = "Online",
            lastSeenAt = "Just now",
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
            processor = request.ProcessorName,
            processorSpeed = request.ProcessorSpeed,
            installedRam = request.InstalledRam,
            usableRam = request.UsableRam,
            graphicsCard = request.GraphicsCard,
            graphicsMemory = request.GraphicsMemory,
            totalStorage = request.TotalStorage,
            usedStorage = request.UsedStorage,
            freeStorage = request.FreeStorage,
            deviceId = request.DeviceId,
            productId = request.ProductId,
            systemType = request.SystemType,
            windowsEdition = request.WindowsEdition,
            windowsVersion = request.WindowsVersion,
            osBuild = request.OsBuild,
            installedOn = request.InstalledOn,
            status = "Online",
            lastSeenAt = "Just now",
            drives = request.Drives
        }
    });
});

app.Run();

record DeviceSystemInfoRequest(
    string? DeviceName,
    string? ProcessorName,
    string? ProcessorSpeed,
    string? InstalledRam,
    string? UsableRam,
    string? GraphicsCard,
    string? GraphicsMemory,
    string? TotalStorage,
    string? UsedStorage,
    string? FreeStorage,
    string? DeviceId,
    string? ProductId,
    string? SystemType,
    string? WindowsEdition,
    string? WindowsVersion,
    string? OsBuild,
    string? InstalledOn,
    List<DriveInfoRequest>? Drives
);

record DriveInfoRequest(
    string? DriveLetter,
    string? DriveType,
    string? FileSystem,
    string? VolumeLabel,
    string? TotalSize,
    string? UsedSpace,
    string? FreeSpace
);