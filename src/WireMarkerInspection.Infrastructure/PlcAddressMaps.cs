using WireMarkerInspection.Application;

namespace WireMarkerInspection.Infrastructure;

/// <summary>
/// Delta DVP device addresses. X and Y are numbered in OCTAL on this family, so X10 is the ninth input,
/// not the tenth: reading it as decimal silently addresses the wrong contact, which looks like working
/// software wired to the wrong signal. X is a physical input and is therefore refused for writes.
/// The table must be checked against the connected PLC before acceptance.
/// </summary>
public sealed class DeltaDvpAddressMap : IPlcAddressMap
{
    private sealed record Device(char Prefix, int Origin, int Limit, int Radix, ModbusArea Area, bool Writable);

    private static readonly Device[] Devices =
    [
        new('S', 0x0000, 1023, 10, ModbusArea.Coil, true),
        new('X', 0x0400, 255, 8, ModbusArea.Coil, false),   // X0-X377 octal; physical inputs are never written
        new('Y', 0x0500, 255, 8, ModbusArea.Coil, true),   // Y0-Y377 octal
        new('T', 0x0600, 255, 10, ModbusArea.Coil, true),
        new('M', 0x0800, 1535, 10, ModbusArea.Coil, true),
        new('C', 0x0E00, 255, 10, ModbusArea.Coil, true),
        new('D', 0x1000, 4095, 10, ModbusArea.HoldingRegister, true),
    ];

    public string Vendor => "delta-dvp";

    public ModbusTarget Translate(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Địa chỉ PLC trống.", nameof(address));
        var text = address.Trim().ToUpperInvariant();
        var prefix = text[0];
        var device = Devices.FirstOrDefault(d => d.Prefix == prefix)
            ?? throw new ArgumentException($"Delta DVP không có vùng '{prefix}'. Dùng S, X, Y, T, M, C hoặc D.", nameof(address));

        var digits = text[1..];
        if (digits.Length == 0) throw new ArgumentException($"Thiếu số thứ tự trong địa chỉ {address}.", nameof(address));
        var number = Parse(digits, device.Radix, address);
        if (number > device.Limit)
            throw new ArgumentException(
                $"{prefix}{digits} vượt dải Delta DVP ({prefix}0–{prefix}{Convert.ToString(device.Limit, device.Radix)}).", nameof(address));

        // M1536 and above, and D4096 and above, live in a second block this table does not cover yet.
        return new(device.Area, checked((ushort)(device.Origin + number)), device.Writable);
    }

    private static int Parse(string digits, int radix, string address)
    {
        var value = 0;
        foreach (var digit in digits)
        {
            var figure = digit - '0';
            if (figure < 0 || figure >= radix)
                throw new ArgumentException(
                    radix == 8
                        ? $"{address} dùng hệ bát phân trên Delta DVP: chỉ nhận chữ số 0–7."
                        : $"{address} phải là số thập phân.", nameof(address));
            value = value * radix + figure;
        }
        return value;
    }
}

/// <summary>Vendor address maps available to the application. Adding a brand means adding one entry.</summary>
public static class PlcAddressMaps
{
    private static readonly IPlcAddressMap[] All = [new DeltaDvpAddressMap()];

    public static IReadOnlyList<string> Vendors => All.Select(m => m.Vendor).ToArray();

    public static IPlcAddressMap For(string vendor) =>
        All.FirstOrDefault(m => string.Equals(m.Vendor, vendor, StringComparison.OrdinalIgnoreCase))
        ?? throw new NotSupportedException($"Chưa hỗ trợ PLC '{vendor}'. Hiện có: {string.Join(", ", Vendors)}.");
}
