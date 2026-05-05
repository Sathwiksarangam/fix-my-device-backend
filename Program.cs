using System.Text.Json;

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

var usersFilePath = Path.Combine(app.Environment.ContentRootPath, "users.json");
var tokensByValue = new Dictionary<string, string>(StringComparer.Ordinal);
var devicesByUserId = new Dictionary<string, List<DeviceRecord>>(StringComparer.Ordinal);
var stateLock = new object();

EnsureUsersFileExists(usersFilePath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFlutterApp");

app.MapGet("/", () => "Fix My Device API is running");

app.MapPost("/api/auth/register", (RegisterRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();

    lock (stateLock)
    {
        var users = LoadUsers(usersFilePath);

        if (users.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new { message = "User already exists." });
        }

        users.Add(new StoredUser(
            Guid.NewGuid().ToString("N"),
            normalizedEmail,
            request.Password.Trim(),
            DateTime.UtcNow));

        SaveUsers(usersFilePath, users);
    }

    return Results.Ok(new { message = "User registered successfully." });
});

app.MapPost("/api/auth/login", (LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var passwordHash = request.Password.Trim();

    lock (stateLock)
    {
        var users = LoadUsers(usersFilePath);
        var user = users.FirstOrDefault(existingUser =>
            existingUser.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase) &&
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
    }
});

app.MapGet("/api/devices", (HttpRequest request) =>
{
    var user = TryGetAuthorizedUser(request, usersFilePath, tokensByValue, stateLock);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    lock (stateLock)
    {
        if (!devicesByUserId.TryGetValue(user.Id, out var devices) || devices.Count == 0)
        {
            return Results.Ok(Array.Empty<object>());
        }

        return Results.Ok(devices);
    }
});

app.MapPost("/api/devices/system-info", (HttpRequest request, DeviceSystemInfoRequest incomingDevice) =>
{
    var user = TryGetAuthorizedUser(request, usersFilePath, tokensByValue, stateLock);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    DeviceRecord storedDevice;

    lock (stateLock)
    {
        if (!devicesByUserId.TryGetValue(user.Id, out var userDevices))
        {
            userDevices = new List<DeviceRecord>();
            devicesByUserId[user.Id] = userDevices;
        }

        var existingDevice = userDevices.FirstOrDefault(device =>
            !string.IsNullOrWhiteSpace(incomingDevice.DeviceId) &&
            device.DeviceId.Equals(incomingDevice.DeviceId, StringComparison.OrdinalIgnoreCase));

        storedDevice = BuildDeviceRecord(incomingDevice, existingDevice?.Id);

        if (existingDevice is null)
        {
            userDevices.Add(storedDevice);
        }
        else
        {
            var index = userDevices.IndexOf(existingDevice);
            userDevices[index] = storedDevice;
        }
    }

    Console.WriteLine("Received and saved system info:");
    Console.WriteLine(JsonSerializer.Serialize(storedDevice, new JsonSerializerOptions
    {
        WriteIndented = true
    }));

    return Results.Ok(new
    {
        message = "System info saved successfully",
        device = storedDevice,
    });
});

app.Run();

static StoredUser? TryGetAuthorizedUser(
    HttpRequest request,
    string usersFilePath,
    Dictionary<string, string> tokensByValue,
    object stateLock)
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

    lock (stateLock)
    {
        if (string.IsNullOrWhiteSpace(token) || !tokensByValue.TryGetValue(token, out var userId))
        {
            return null;
        }

        var users = LoadUsers(usersFilePath);
        return users.FirstOrDefault(user => user.Id == userId);
    }
}

static DeviceRecord BuildDeviceRecord(DeviceSystemInfoRequest request, string? existingId)
{
    return new DeviceRecord(
        existingId ?? Guid.NewGuid().ToString("N"),
        ValueOrUnknown(request.DeviceName),
        ValueOrUnknown(request.Processor),
        ValueOrUnknown(request.ProcessorSpeed),
        ValueOrUnknown(request.InstalledRam),
        ValueOrUnknown(request.UsableRam),
        ValueOrUnknown(request.GraphicsCard),
        ValueOrUnknown(request.GraphicsMemory),
        ValueOrUnknown(request.TotalStorage),
        ValueOrUnknown(request.UsedStorage),
        ValueOrUnknown(request.FreeStorage),
        ValueOrUnknown(request.DeviceId),
        ValueOrUnknown(request.ProductId),
        ValueOrUnknown(request.SystemType),
        ValueOrUnknown(request.WindowsEdition),
        ValueOrUnknown(request.WindowsVersion),
        ValueOrUnknown(request.OsBuild),
        ValueOrUnknown(request.InstalledOn),
        string.IsNullOrWhiteSpace(request.Status) ? "Online" : request.Status.Trim(),
        DateTimeOffset.UtcNow.ToString("O"),
        (request.Drives ?? new List<DriveInfoRequest>())
            .Select(drive => new DriveInfoRequest(
                ValueOrUnknown(drive.DriveLetter),
                ValueOrUnknown(drive.DriveType),
                ValueOrUnknown(drive.FileSystem),
                ValueOrUnknown(drive.VolumeLabel),
                ValueOrUnknown(drive.TotalSize),
                ValueOrUnknown(drive.UsedSpace),
                ValueOrUnknown(drive.FreeSpace)))
            .ToList());
}

static void EnsureUsersFileExists(string usersFilePath)
{
    if (File.Exists(usersFilePath))
    {
        return;
    }

    File.WriteAllText(usersFilePath, "[]");
}

static List<StoredUser> LoadUsers(string usersFilePath)
{
    try
    {
        var json = File.ReadAllText(usersFilePath);
        var users = JsonSerializer.Deserialize<List<StoredUser>>(json);
        return users ?? new List<StoredUser>();
    }
    catch
    {
        return new List<StoredUser>();
    }
}

static void SaveUsers(string usersFilePath, List<StoredUser> users)
{
    var json = JsonSerializer.Serialize(users, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(usersFilePath, json);
}

static string ValueOrUnknown(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}

record RegisterRequest(string? Email, string? Password);

record LoginRequest(string? Email, string? Password);

record StoredUser(
    string Id,
    string Email,
    string PasswordHash,
    DateTime CreatedAt
);

record DeviceRecord(
    string Id,
    string DeviceName,
    string Processor,
    string ProcessorSpeed,
    string InstalledRam,
    string UsableRam,
    string GraphicsCard,
    string GraphicsMemory,
    string TotalStorage,
    string UsedStorage,
    string FreeStorage,
    string DeviceId,
    string ProductId,
    string SystemType,
    string WindowsEdition,
    string WindowsVersion,
    string OsBuild,
    string InstalledOn,
    string Status,
    string LastSeenAt,
    List<DriveInfoRequest> Drives
);

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
