namespace fix_my_device_backend.Models;

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public string ProcessorSpeed { get; set; } = string.Empty;
    public string InstalledRam { get; set; } = string.Empty;
    public string UsableRam { get; set; } = string.Empty;
    public string GraphicsCard { get; set; } = string.Empty;
    public string GraphicsMemory { get; set; } = string.Empty;
    public string TotalStorage { get; set; } = string.Empty;
    public string UsedStorage { get; set; } = string.Empty;
    public string FreeStorage { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string SystemType { get; set; } = string.Empty;
    public string WindowsEdition { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public string InstalledOn { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    public List<DeviceDrive> Drives { get; set; } = new();
}
