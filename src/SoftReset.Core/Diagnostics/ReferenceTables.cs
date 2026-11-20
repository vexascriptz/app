using System.Globalization;
using System.Text.RegularExpressions;

namespace SoftReset.Core.Diagnostics;

/// <summary>Stop codes documented in Microsoft's "Bug Check Code Reference".</summary>
public static partial class BugCheckCodes
{
    private static readonly Dictionary<uint, (string Name, string Hint)> Codes = new()
    {
        [0x0000000A] = ("IRQL_NOT_LESS_OR_EQUAL", "Usually a faulty driver or failing RAM."),
        [0x0000001A] = ("MEMORY_MANAGEMENT", "Often failing or unstable RAM (check XMP/EXPO) or a driver corrupting memory."),
        [0x0000001E] = ("KMODE_EXCEPTION_NOT_HANDLED", "A kernel-mode driver raised an exception it did not handle."),
        [0x00000019] = ("BAD_POOL_HEADER", "A driver corrupted kernel memory pool."),
        [0x0000003B] = ("SYSTEM_SERVICE_EXCEPTION", "Commonly graphics, antivirus, or other third-party drivers."),
        [0x00000050] = ("PAGE_FAULT_IN_NONPAGED_AREA", "Faulty driver, RAM, or antivirus filter driver."),
        [0x0000007A] = ("KERNEL_DATA_INPAGE_ERROR", "Data could not be read from disk — check disk health and cables."),
        [0x0000007E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "A system thread raised an exception — usually a driver."),
        [0x0000009F] = ("DRIVER_POWER_STATE_FAILURE", "A driver failed during sleep/wake — update chipset, network, and GPU drivers."),
        [0x000000C2] = ("BAD_POOL_CALLER", "A driver made a bad memory pool request."),
        [0x000000D1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "A driver accessed invalid memory — network and storage drivers are common causes."),
        [0x000000EF] = ("CRITICAL_PROCESS_DIED", "A critical Windows process ended — check disk health and run SFC/DISM."),
        [0x000000F4] = ("CRITICAL_OBJECT_TERMINATION", "A critical process or thread ended — often storage problems."),
        [0x00000101] = ("CLOCK_WATCHDOG_TIMEOUT", "A CPU core stopped responding — unstable overclock, voltage, or BIOS issue."),
        [0x00000109] = ("CRITICAL_STRUCTURE_CORRUPTION", "Kernel code or data was modified — faulty driver or RAM."),
        [0x00000113] = ("VIDEO_DXGKRNL_FATAL_ERROR", "Graphics kernel failure — reinstall the GPU driver."),
        [0x00000116] = ("VIDEO_TDR_FAILURE", "The GPU driver failed to recover after a timeout — driver, GPU overclock, power, or overheating."),
        [0x00000117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "The GPU stopped responding."),
        [0x00000119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "GPU scheduler failure — GPU driver or hardware."),
        [0x00000124] = ("WHEA_UNCORRECTABLE_ERROR", "A hardware error — CPU, RAM, unstable overclock/undervolt, or power delivery."),
        [0x0000012B] = ("FAULTY_HARDWARE_CORRUPTED_PAGE", "Hardware memory corruption detected — test RAM."),
        [0x00000133] = ("DPC_WATCHDOG_VIOLATION", "A driver took too long — often storage (SSD firmware/driver) or network drivers."),
        [0x00000139] = ("KERNEL_SECURITY_CHECK_FAILURE", "Kernel data corruption — drivers, RAM, or system files."),
        [0x00000141] = ("VIDEO_ENGINE_TIMEOUT_DETECTED", "A GPU engine timed out."),
        [0x00000154] = ("UNEXPECTED_STORE_EXCEPTION", "Memory store component failure — often storage or RAM."),
        [0x0000015F] = ("CONNECTED_STANDBY_WATCHDOG_TIMEOUT_LIVEDUMP", "Modern Standby watchdog — firmware or driver issue."),
        [0x000001D3] = ("DRIVER_PNP_WATCHDOG", "A Plug and Play driver stopped responding."),
        [0x0000003D] = ("INTERRUPT_EXCEPTION_NOT_HANDLED", "An interrupt exception — hardware or driver."),
        [0x00000077] = ("KERNEL_STACK_INPAGE_ERROR", "Kernel data could not be read from the page file — disk health."),
        [0x000000BE] = ("ATTEMPTED_WRITE_TO_READONLY_MEMORY", "A driver wrote to read-only memory."),
        [0x000000C5] = ("DRIVER_CORRUPTED_EXPOOL", "A driver corrupted the memory pool."),
        [0x000000FC] = ("ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", "A driver attempted to execute non-executable memory."),
        [0x0000013A] = ("KERNEL_MODE_HEAP_CORRUPTION", "Kernel heap corruption — usually a driver."),
        [0xC000021A] = ("STATUS_SYSTEM_PROCESS_TERMINATED", "A user-mode subsystem failed — system file corruption or a bad update."),
    };

    [GeneratedRegex(@"0x[0-9a-fA-F]{8}")]
    private static partial Regex HexCode();

    public static uint? ParseFromText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = HexCode().Match(text);
        return m.Success ? uint.Parse(m.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    public static uint? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex;
        if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)) return unchecked((uint)dec);
        return null;
    }

    public static string Describe(uint code) =>
        Codes.TryGetValue(code, out var c) ? $"0x{code:X8} {c.Name}" : $"0x{code:X8}";

    public static string Hint(uint code) =>
        Codes.TryGetValue(code, out var c) ? c.Hint : "Look the stop code up in Microsoft's Bug Check Code Reference and inspect the minidump with WinDbg.";

    public static bool IsGraphicsRelated(uint code) => code is 0x113 or 0x116 or 0x117 or 0x119 or 0x141 or 0x10E;
    public static bool IsHardwareRelated(uint code) => code is 0x124 or 0x12B or 0x101 or 0x7A or 0x77;
}

/// <summary>Common Windows Update error codes and the documented remediation.</summary>
public static partial class WindowsUpdateErrors
{
    private static readonly Dictionary<uint, string> Codes = new()
    {
        [0x80070002] = "ERROR_FILE_NOT_FOUND — update files missing or corrupted download; resetting the update cache usually helps.",
        [0x80070003] = "ERROR_PATH_NOT_FOUND — corrupted update cache.",
        [0x80070005] = "E_ACCESSDENIED — permissions problem, often third-party security software.",
        [0x8007000D] = "ERROR_INVALID_DATA — corrupted update files.",
        [0x80070020] = "ERROR_SHARING_VIOLATION — another program (often antivirus) locked a file.",
        [0x8007045B] = "ERROR_SHUTDOWN_IN_PROGRESS — the PC was shutting down.",
        [0x80070570] = "ERROR_FILE_CORRUPT — corrupted files; check disk health and run DISM/SFC.",
        [0x80070643] = "ERROR_INSTALL_FAILURE — often .NET or the WinRE partition (KB5034441 family) is too small.",
        [0x80070652] = "ERROR_INSTALL_ALREADY_RUNNING — another installation is in progress.",
        [0x8007371B] = "ERROR_SXS_TRANSACTION_CLOSURE_INCOMPLETE — component store corruption; run DISM /RestoreHealth.",
        [0x80073712] = "ERROR_SXS_COMPONENT_STORE_CORRUPT — component store damaged; run DISM /RestoreHealth.",
        [0x800F081F] = "CBS_E_SOURCE_MISSING — DISM could not find repair source files.",
        [0x800F0831] = "CBS_E_STORE_CORRUPTION — a prerequisite package is missing; component store repair needed.",
        [0x800F0922] = "CBS_E_INSTALLERS_FAILED — often too little space on the System Reserved/EFI partition, or VPN interference.",
        [0x800F0988] = "PSFX_E_INVALID_DELTA_COMBINATION — run DISM component cleanup and retry.",
        [0x80240034] = "WU_E_DOWNLOAD_FAILED — the download failed; check network and update cache.",
        [0x8024402C] = "WU_E_PT_WINHTTP_NAME_NOT_RESOLVED — DNS/proxy problem reaching Windows Update.",
        [0x80244022] = "WU_E_PT_HTTP_STATUS_SERVICE_UNAVAIL — the update service was temporarily unavailable.",
        [0x8024A105] = "WU_E_AU_NO_REGISTERED_SERVICE — update agent registration problem; reset Windows Update components.",
        [0x80242016] = "WU_E_UH_POSTREBOOTUNEXPECTEDSTATE — update was interrupted around a restart.",
        [0x8024200B] = "WU_E_UH_INSTALLERFAILURE — the update handler failed; retry after reset.",
        [0xC1900101] = "Feature update rolled back — almost always a driver problem (often storage, GPU, or antivirus).",
        [0xC1900208] = "An incompatible app is blocking the feature update.",
        [0x8007001F] = "ERROR_GEN_FAILURE — a device driver failed; update drivers before retrying.",
    };

    [GeneratedRegex(@"0x[0-9a-fA-F]{8}")]
    private static partial Regex HexCode();

    public static uint? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = HexCode().Match(text);
        return m.Success ? uint.Parse(m.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    public static string Describe(uint code) =>
        Codes.TryGetValue(code, out var d) ? $"0x{code:X8}: {d}" : $"0x{code:X8}";
}
