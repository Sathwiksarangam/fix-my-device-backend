namespace fix_my_device_backend.Models;

public sealed class DeviceDrive
{
    public Guid Id { get; set; }
    public Guid DeviceEntityId { get; set; }
    public Device? Device { get; set; }
    public string DriveLetter { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string TotalSize { get; set; } = string.Empty;
    public string UsedSpace { get; set; } = string.Empty;
    public string FreeSpace { get; set; } = string.Empty;
}
