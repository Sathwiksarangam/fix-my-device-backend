using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
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

var connectionString = Environment.GetEnvironmentVariable("SUPABASE_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("SUPABASE_DB_CONNECTION environment variable is missing.");
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".exe"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/downloads"))
        {
            var fileName = Path.GetFileName(context.File.Name);
            context.Context.Response.Headers.ContentDisposition =
                $"attachment; filename=\"{fileName}\"";
        }
    },
});

app.UseCors("AllowFlutterApp");

app.MapGet("/", () => "Fix My Device API is running");

app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var passwordHash = request.Password.Trim();
    var token = Guid.NewGuid().ToString("N");
    var agentSetupCode = GenerateAgentSetupCode();
    var userId = Guid.NewGuid();
    var createdAt = DateTimeOffset.UtcNow;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var existsCommand = new NpgsqlCommand(
        """
        select 1
        from app_users
        where lower(email) = @email
        limit 1
        """,
        connection);
    existsCommand.Parameters.AddWithValue("email", normalizedEmail);

    var existingUser = await existsCommand.ExecuteScalarAsync();
    if (existingUser is not null)
    {
        return Results.Conflict(new { message = "User already exists." });
    }

    await using var insertCommand = new NpgsqlCommand(
        """
        insert into app_users (id, email, password_hash, token, agent_setup_code, created_at)
        values (@id, @email, @password_hash, @token, @agent_setup_code, @created_at)
        """,
        connection);

    insertCommand.Parameters.AddWithValue("id", userId);
    insertCommand.Parameters.AddWithValue("email", normalizedEmail);
    insertCommand.Parameters.AddWithValue("password_hash", passwordHash);
    insertCommand.Parameters.AddWithValue("token", token);
    insertCommand.Parameters.AddWithValue("agent_setup_code", agentSetupCode);
    insertCommand.Parameters.AddWithValue("created_at", createdAt);

    await insertCommand.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        message = "User registered successfully",
        token,
        email = normalizedEmail,
        agentSetupCode,
    });
});

app.MapPost("/api/auth/login", async (LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var passwordHash = request.Password.Trim();

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var selectCommand = new NpgsqlCommand(
        """
        select id, email, token, agent_setup_code
        from app_users
        where lower(email) = @email and password_hash = @password_hash
        limit 1
        """,
        connection);

    selectCommand.Parameters.AddWithValue("email", normalizedEmail);
    selectCommand.Parameters.AddWithValue("password_hash", passwordHash);

    await using var reader = await selectCommand.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.Unauthorized();
    }

    var userId = reader.GetGuid(reader.GetOrdinal("id"));
    var email = reader.GetString(reader.GetOrdinal("email"));
    var token = reader.IsDBNull(reader.GetOrdinal("token"))
        ? string.Empty
        : reader.GetString(reader.GetOrdinal("token"));
    var agentSetupCode = reader.IsDBNull(reader.GetOrdinal("agent_setup_code"))
        ? string.Empty
        : reader.GetString(reader.GetOrdinal("agent_setup_code"));
    await reader.CloseAsync();

    if (string.IsNullOrWhiteSpace(token))
    {
        token = Guid.NewGuid().ToString("N");
    }

    if (string.IsNullOrWhiteSpace(agentSetupCode))
    {
        agentSetupCode = GenerateAgentSetupCode();
    }

    await using var updateCommand = new NpgsqlCommand(
        """
        update app_users
        set token = @token,
            agent_setup_code = @agent_setup_code
        where id = @id
        """,
        connection);

    updateCommand.Parameters.AddWithValue("id", userId);
    updateCommand.Parameters.AddWithValue("token", token);
    updateCommand.Parameters.AddWithValue("agent_setup_code", agentSetupCode);
    await updateCommand.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        token,
        email,
        agentSetupCode,
    });
});

app.MapGet("/api/agent/setup-code", async (HttpRequest request) =>
{
    var user = await TryGetAuthorizedUserAsync(request, connectionString);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var agentSetupCode = string.IsNullOrWhiteSpace(user.AgentSetupCode)
        ? GenerateAgentSetupCode()
        : user.AgentSetupCode;

    if (agentSetupCode != user.AgentSetupCode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var updateCommand = new NpgsqlCommand(
            "update app_users set agent_setup_code = @agent_setup_code where id = @id",
            connection);
        updateCommand.Parameters.AddWithValue("agent_setup_code", agentSetupCode);
        updateCommand.Parameters.AddWithValue("id", user.Id);
        await updateCommand.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        agentSetupCode,
    });
});

app.MapGet("/api/devices", async (HttpRequest request) =>
{
    var user = await TryGetAuthorizedUserAsync(request, connectionString);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var devices = new List<DeviceRecord>();

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(
        """
        select id, user_id, device_name, processor, processor_speed, installed_ram,
               usable_ram, graphics_card, graphics_memory, total_storage, used_storage,
               free_storage, device_id, product_id, system_type, windows_edition,
               windows_version, os_build, installed_on, status, last_seen_at, drives_json
        from devices
        where user_id = @user_id
        order by last_seen_at desc nulls last
        """,
        connection);
    command.Parameters.AddWithValue("user_id", user.Id);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        devices.Add(new DeviceRecord(
            reader["id"]?.ToString() ?? string.Empty,
            reader["device_name"]?.ToString() ?? "Unknown",
            reader["processor"]?.ToString() ?? "Unknown",
            reader["processor_speed"]?.ToString() ?? "Unknown",
            reader["installed_ram"]?.ToString() ?? "Unknown",
            reader["usable_ram"]?.ToString() ?? "Unknown",
            reader["graphics_card"]?.ToString() ?? "Unknown",
            reader["graphics_memory"]?.ToString() ?? "Unknown",
            reader["total_storage"]?.ToString() ?? "Unknown",
            reader["used_storage"]?.ToString() ?? "Unknown",
            reader["free_storage"]?.ToString() ?? "Unknown",
            reader["device_id"]?.ToString() ?? "Unknown",
            reader["product_id"]?.ToString() ?? "Unknown",
            reader["system_type"]?.ToString() ?? "Unknown",
            reader["windows_edition"]?.ToString() ?? "Unknown",
            reader["windows_version"]?.ToString() ?? "Unknown",
            reader["os_build"]?.ToString() ?? "Unknown",
            reader["installed_on"]?.ToString() ?? "Unknown",
            reader["status"]?.ToString() ?? "Online",
            ReadDateTimeAsIsoString(reader, "last_seen_at"),
            DeserializeDrives(reader["drives_json"]?.ToString())));
    }

    return Results.Ok(devices);
});

app.MapPost("/api/devices/system-info-by-code", async (DeviceSystemInfoRequest incomingDevice) =>
{
    if (incomingDevice is null || string.IsNullOrWhiteSpace(incomingDevice.AgentSetupCode))
    {
        return Results.BadRequest(new { message = "Agent setup code is required." });
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    var user = await FindUserBySetupCodeAsync(connection, incomingDevice.AgentSetupCode);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var normalizedDeviceId = ValueOrUnknown(incomingDevice.DeviceId);
    var storedDeviceId = await FindExistingDeviceRowIdAsync(connection, user.Id, normalizedDeviceId)
        ?? Guid.NewGuid().ToString("N");

    var drives = NormalizeDrives(incomingDevice.Drives);
    var drivesJson = JsonSerializer.Serialize(drives);
    var lastSeenAt = DateTimeOffset.UtcNow;
    var status = string.IsNullOrWhiteSpace(incomingDevice.Status) ? "Online" : incomingDevice.Status.Trim();

    var upsertSql = await FindExistingDeviceRowIdAsync(connection, user.Id, normalizedDeviceId) is null
        ? """
          insert into devices (
              id, user_id, device_name, processor, processor_speed, installed_ram,
              usable_ram, graphics_card, graphics_memory, total_storage, used_storage,
              free_storage, device_id, product_id, system_type, windows_edition,
              windows_version, os_build, installed_on, status, last_seen_at, drives_json)
          values (
              @id, @user_id, @device_name, @processor, @processor_speed, @installed_ram,
              @usable_ram, @graphics_card, @graphics_memory, @total_storage, @used_storage,
              @free_storage, @device_id, @product_id, @system_type, @windows_edition,
              @windows_version, @os_build, @installed_on, @status, @last_seen_at, @drives_json)
          """
        : """
          update devices
          set device_name = @device_name,
              processor = @processor,
              processor_speed = @processor_speed,
              installed_ram = @installed_ram,
              usable_ram = @usable_ram,
              graphics_card = @graphics_card,
              graphics_memory = @graphics_memory,
              total_storage = @total_storage,
              used_storage = @used_storage,
              free_storage = @free_storage,
              product_id = @product_id,
              system_type = @system_type,
              windows_edition = @windows_edition,
              windows_version = @windows_version,
              os_build = @os_build,
              installed_on = @installed_on,
              status = @status,
              last_seen_at = @last_seen_at,
              drives_json = @drives_json
          where id = @id and user_id = @user_id
          """;

    await using var upsertCommand = new NpgsqlCommand(upsertSql, connection);
    upsertCommand.Parameters.AddWithValue("id", storedDeviceId);
    upsertCommand.Parameters.AddWithValue("user_id", user.Id);
    upsertCommand.Parameters.AddWithValue("device_name", ValueOrUnknown(incomingDevice.DeviceName));
    upsertCommand.Parameters.AddWithValue("processor", ValueOrUnknown(incomingDevice.Processor));
    upsertCommand.Parameters.AddWithValue("processor_speed", ValueOrUnknown(incomingDevice.ProcessorSpeed));
    upsertCommand.Parameters.AddWithValue("installed_ram", ValueOrUnknown(incomingDevice.InstalledRam));
    upsertCommand.Parameters.AddWithValue("usable_ram", ValueOrUnknown(incomingDevice.UsableRam));
    upsertCommand.Parameters.AddWithValue("graphics_card", ValueOrUnknown(incomingDevice.GraphicsCard));
    upsertCommand.Parameters.AddWithValue("graphics_memory", ValueOrUnknown(incomingDevice.GraphicsMemory));
    upsertCommand.Parameters.AddWithValue("total_storage", ValueOrUnknown(incomingDevice.TotalStorage));
    upsertCommand.Parameters.AddWithValue("used_storage", ValueOrUnknown(incomingDevice.UsedStorage));
    upsertCommand.Parameters.AddWithValue("free_storage", ValueOrUnknown(incomingDevice.FreeStorage));
    upsertCommand.Parameters.AddWithValue("device_id", normalizedDeviceId);
    upsertCommand.Parameters.AddWithValue("product_id", ValueOrUnknown(incomingDevice.ProductId));
    upsertCommand.Parameters.AddWithValue("system_type", ValueOrUnknown(incomingDevice.SystemType));
    upsertCommand.Parameters.AddWithValue("windows_edition", ValueOrUnknown(incomingDevice.WindowsEdition));
    upsertCommand.Parameters.AddWithValue("windows_version", ValueOrUnknown(incomingDevice.WindowsVersion));
    upsertCommand.Parameters.AddWithValue("os_build", ValueOrUnknown(incomingDevice.OsBuild));
    upsertCommand.Parameters.AddWithValue("installed_on", ValueOrUnknown(incomingDevice.InstalledOn));
    upsertCommand.Parameters.AddWithValue("status", status);
    upsertCommand.Parameters.AddWithValue("last_seen_at", lastSeenAt);
    upsertCommand.Parameters.AddWithValue("drives_json", drivesJson);

    await upsertCommand.ExecuteNonQueryAsync();

    var storedDevice = new DeviceRecord(
        storedDeviceId,
        ValueOrUnknown(incomingDevice.DeviceName),
        ValueOrUnknown(incomingDevice.Processor),
        ValueOrUnknown(incomingDevice.ProcessorSpeed),
        ValueOrUnknown(incomingDevice.InstalledRam),
        ValueOrUnknown(incomingDevice.UsableRam),
        ValueOrUnknown(incomingDevice.GraphicsCard),
        ValueOrUnknown(incomingDevice.GraphicsMemory),
        ValueOrUnknown(incomingDevice.TotalStorage),
        ValueOrUnknown(incomingDevice.UsedStorage),
        ValueOrUnknown(incomingDevice.FreeStorage),
        normalizedDeviceId,
        ValueOrUnknown(incomingDevice.ProductId),
        ValueOrUnknown(incomingDevice.SystemType),
        ValueOrUnknown(incomingDevice.WindowsEdition),
        ValueOrUnknown(incomingDevice.WindowsVersion),
        ValueOrUnknown(incomingDevice.OsBuild),
        ValueOrUnknown(incomingDevice.InstalledOn),
        status,
        lastSeenAt.ToString("O"),
        drives);

    return Results.Ok(new
    {
        message = "System info saved successfully",
        device = storedDevice,
    });
});

app.Run();

static async Task<AppUserRecord?> TryGetAuthorizedUserAsync(HttpRequest request, string connectionString)
{
    if (!request.Headers.TryGetValue("Authorization", out var authorizationHeader))
    {
        return null;
    }

    var headerValue = authorizationHeader.ToString();
    if (string.IsNullOrWhiteSpace(headerValue) ||
        !headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = headerValue[7..].Trim();
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(
        """
        select id, email, token, agent_setup_code, created_at
        from app_users
        where token = @token
        limit 1
        """,
        connection);
    command.Parameters.AddWithValue("token", token);

    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
    return await reader.ReadAsync() ? ReadAppUser(reader) : null;
}

static async Task<AppUserRecord?> FindUserBySetupCodeAsync(NpgsqlConnection connection, string? agentSetupCode)
{
    var normalizedCode = NormalizeSetupCode(agentSetupCode);
    if (string.IsNullOrWhiteSpace(normalizedCode))
    {
        return null;
    }

    await using var command = new NpgsqlCommand(
        """
        select id, email, token, agent_setup_code, created_at
        from app_users
        where upper(agent_setup_code) = @agent_setup_code
        limit 1
        """,
        connection);
    command.Parameters.AddWithValue("agent_setup_code", normalizedCode);

    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
    return await reader.ReadAsync() ? ReadAppUser(reader) : null;
}

static async Task<string?> FindExistingDeviceRowIdAsync(NpgsqlConnection connection, Guid userId, string normalizedDeviceId)
{
    await using var command = new NpgsqlCommand(
        """
        select id
        from devices
        where user_id = @user_id and device_id = @device_id
        limit 1
        """,
        connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("device_id", normalizedDeviceId);

    var result = await command.ExecuteScalarAsync();
    return result?.ToString();
}

static AppUserRecord ReadAppUser(NpgsqlDataReader reader)
{
    return new AppUserRecord(
        reader.GetGuid(reader.GetOrdinal("id")),
        reader["email"]?.ToString() ?? string.Empty,
        reader["token"]?.ToString() ?? string.Empty,
        reader["agent_setup_code"]?.ToString() ?? string.Empty,
        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
}

static string ReadDateTimeAsIsoString(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    if (reader.IsDBNull(ordinal))
    {
        return string.Empty;
    }

    var value = reader.GetFieldValue<DateTimeOffset>(ordinal);
    return value.ToString("O");
}

static List<DriveInfoRequest> DeserializeDrives(string? drivesJson)
{
    if (string.IsNullOrWhiteSpace(drivesJson))
    {
        return new List<DriveInfoRequest>();
    }

    try
    {
        return JsonSerializer.Deserialize<List<DriveInfoRequest>>(drivesJson) ?? new List<DriveInfoRequest>();
    }
    catch
    {
        return new List<DriveInfoRequest>();
    }
}

static List<DriveInfoRequest> NormalizeDrives(List<DriveInfoRequest>? drives)
{
    return (drives ?? new List<DriveInfoRequest>())
        .Select(drive => new DriveInfoRequest(
            ValueOrUnknown(drive.DriveLetter),
            ValueOrUnknown(drive.DriveType),
            ValueOrUnknown(drive.FileSystem),
            ValueOrUnknown(drive.VolumeLabel),
            ValueOrUnknown(drive.TotalSize),
            ValueOrUnknown(drive.UsedSpace),
            ValueOrUnknown(drive.FreeSpace)))
        .ToList();
}

static string GenerateAgentSetupCode()
{
    var raw = Guid.NewGuid().ToString("N").ToUpperInvariant();
    return $"FMD-{raw[..4]}-{raw[4..8]}";
}

static string NormalizeSetupCode(string? setupCode)
{
    return string.IsNullOrWhiteSpace(setupCode)
        ? string.Empty
        : setupCode.Trim().ToUpperInvariant();
}

static string ValueOrUnknown(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}

record RegisterRequest(string? Email, string? Password);

record LoginRequest(string? Email, string? Password);

record AppUserRecord(
    Guid Id,
    string Email,
    string Token,
    string AgentSetupCode,
    DateTimeOffset CreatedAt);

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
    List<DriveInfoRequest> Drives);

record DeviceSystemInfoRequest(
    string? AgentSetupCode,
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
    List<DriveInfoRequest>? Drives);

record DriveInfoRequest(
    string? DriveLetter,
    string? DriveType,
    string? FileSystem,
    string? VolumeLabel,
    string? TotalSize,
    string? UsedSpace,
    string? FreeSpace);
