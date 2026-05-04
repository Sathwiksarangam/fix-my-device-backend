using System.Text.Json;
using fix_my_device_backend.Data;
using fix_my_device_backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(BuildConnectionString(rawConnectionString)));

var app = builder.Build();
var tokensByValue = new Dictionary<string, Guid>(StringComparer.Ordinal);


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFlutterApp");

app.MapGet("/", () => "Fix My Device API is running");

app.MapPost("/api/auth/register", async (RegisterRequest request, AppDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();

    var existingUser = await dbContext.Users
        .FirstOrDefaultAsync(user => user.Email == normalizedEmail);

    if (existingUser is not null)
    {
        return Results.Conflict(new { message = "User already exists." });
    }

    var user = new User
    {
        Id = Guid.NewGuid(),
        Email = normalizedEmail,
        PasswordHash = request.Password.Trim(),
        CreatedAt = DateTime.UtcNow,
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();

    return Results.Ok(new { message = "User registered successfully." });
});

app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var passwordHash = request.Password.Trim();

    var user = await dbContext.Users
        .FirstOrDefaultAsync(existingUser =>
            existingUser.Email == normalizedEmail &&
            existingUser.PasswordHash == passwordHash);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var token = Guid.NewGuid().ToString("N");
    tokensByValue[token] = user.Id;

    return Results.Ok(new
    {
        token,
        email = user.Email,
    });
});

app.MapGet("/api/devices", async (HttpRequest request, AppDbContext dbContext) =>
{
    var user = await TryGetAuthorizedUserAsync(request, dbContext, tokensByValue);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var devices = await dbContext.Devices
        .AsNoTracking()
        .Include(device => device.Drives)
        .Where(device => device.UserId == user.Id)
        .OrderByDescending(device => device.LastSeenAt)
        .ToListAsync();

    return Results.Ok(devices);
});

app.MapPost("/api/devices/system-info", async (
    HttpRequest request,
    DeviceSystemInfoRequest incomingDevice,
    AppDbContext dbContext) =>
{
    var user = await TryGetAuthorizedUserAsync(request, dbContext, tokensByValue);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var normalizedDeviceId = ValueOrUnknown(incomingDevice.DeviceId);

    var device = await dbContext.Devices
        .Include(existingDevice => existingDevice.Drives)
        .FirstOrDefaultAsync(existingDevice =>
            existingDevice.UserId == user.Id &&
            existingDevice.DeviceId == normalizedDeviceId);

    if (device is null)
    {
        device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = normalizedDeviceId,
        };

        dbContext.Devices.Add(device);
    }

    device.DeviceName = ValueOrUnknown(incomingDevice.DeviceName);
    device.Processor = ValueOrUnknown(incomingDevice.Processor);
    device.ProcessorSpeed = ValueOrUnknown(incomingDevice.ProcessorSpeed);
    device.InstalledRam = ValueOrUnknown(incomingDevice.InstalledRam);
    device.UsableRam = ValueOrUnknown(incomingDevice.UsableRam);
    device.GraphicsCard = ValueOrUnknown(incomingDevice.GraphicsCard);
    device.GraphicsMemory = ValueOrUnknown(incomingDevice.GraphicsMemory);
    device.TotalStorage = ValueOrUnknown(incomingDevice.TotalStorage);
    device.UsedStorage = ValueOrUnknown(incomingDevice.UsedStorage);
    device.FreeStorage = ValueOrUnknown(incomingDevice.FreeStorage);
    device.ProductId = ValueOrUnknown(incomingDevice.ProductId);
    device.SystemType = ValueOrUnknown(incomingDevice.SystemType);
    device.WindowsEdition = ValueOrUnknown(incomingDevice.WindowsEdition);
    device.WindowsVersion = ValueOrUnknown(incomingDevice.WindowsVersion);
    device.OsBuild = ValueOrUnknown(incomingDevice.OsBuild);
    device.InstalledOn = ValueOrUnknown(incomingDevice.InstalledOn);
    device.Status = string.IsNullOrWhiteSpace(incomingDevice.Status)
        ? "Online"
        : incomingDevice.Status.Trim();
    device.LastSeenAt = DateTimeOffset.UtcNow.ToString("O");

    dbContext.DeviceDrives.RemoveRange(device.Drives);
    device.Drives.Clear();

    foreach (var drive in incomingDevice.Drives ?? new List<DriveInfoRequest>())
    {
        device.Drives.Add(new DeviceDrive
        {
            Id = Guid.NewGuid(),
            DeviceEntityId = device.Id,
            DriveLetter = ValueOrUnknown(drive.DriveLetter),
            DriveType = ValueOrUnknown(drive.DriveType),
            FileSystem = ValueOrUnknown(drive.FileSystem),
            VolumeLabel = ValueOrUnknown(drive.VolumeLabel),
            TotalSize = ValueOrUnknown(drive.TotalSize),
            UsedSpace = ValueOrUnknown(drive.UsedSpace),
            FreeSpace = ValueOrUnknown(drive.FreeSpace),
        });
    }

    await dbContext.SaveChangesAsync();

    var responseDevice = await dbContext.Devices
        .AsNoTracking()
        .Include(savedDevice => savedDevice.Drives)
        .FirstAsync(savedDevice => savedDevice.Id == device.Id);

    Console.WriteLine("Received and saved system info:");
    Console.WriteLine(JsonSerializer.Serialize(responseDevice, new JsonSerializerOptions
    {
        WriteIndented = true
    }));

    return Results.Ok(new
    {
        message = "System info saved successfully",
        device = responseDevice,
    });
});

app.Run();

static async Task<User?> TryGetAuthorizedUserAsync(
    HttpRequest request,
    AppDbContext dbContext,
    Dictionary<string, Guid> tokensByValue)
{
    if (!request.Headers.TryGetValue("Authorization", out var authorizationHeader))
    {
        return null;
    }

    var headerValue = authorizationHeader.ToString();

    if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = headerValue[7..].Trim();

    if (string.IsNullOrWhiteSpace(token) || !tokensByValue.TryGetValue(token, out var userId))
    {
        return null;
    }

    return await dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId);
}

static string BuildConnectionString(string rawConnectionString)
{
    if (!rawConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
        !rawConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
    {
        return rawConnectionString;
    }

    var connectionUri = new Uri(rawConnectionString);
    var userInfoParts = connectionUri.UserInfo.Split(':', 2);
    var databaseName = connectionUri.AbsolutePath.Trim('/');

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = connectionUri.Host,
        Port = connectionUri.Port,
        Username = userInfoParts.ElementAtOrDefault(0) ?? string.Empty,
        Password = userInfoParts.ElementAtOrDefault(1) ?? string.Empty,
        Database = string.IsNullOrWhiteSpace(databaseName) ? "postgres" : databaseName,
        SslMode = SslMode.Require,
        TrustServerCertificate = true,
    };

    return builder.ConnectionString;
}

static string ValueOrUnknown(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}

record RegisterRequest(string? Email, string? Password);

record LoginRequest(string? Email, string? Password);

record DeviceSystemInfoRequest(
    string? DeviceName,
    string? Processor,
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
    string? Status,
    string? LastSeenAt,
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
