using System.Text;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Builds tiny, valid 32-bit PE images in memory for tests (never executed, no game files involved).
/// The image has one section that holds either a VS_VERSION_INFO resource or plain data.
/// </summary>
public static class SyntheticPe
{
    private const int FileAlignment = 0x200;
    private const int SectionRva = 0x1000;

    /// <summary>A PE image whose version resource carries <paramref name="version"/> (e.g. 3.3.5.12340).</summary>
    public static byte[] WithVersion(Version version) => Build(BuildResourceSection(version), hasResources: true);

    /// <summary>A valid PE image with no resource directory at all.</summary>
    public static byte[] WithoutVersionResource() => Build(new byte[16], hasResources: false);

    private static byte[] Build(byte[] section, bool hasResources)
    {
        var rawSize = (section.Length + FileAlignment - 1) / FileAlignment * FileAlignment;
        var file = new byte[FileAlignment + rawSize];
        using var ms = new MemoryStream(file);
        using var w = new BinaryWriter(ms);

        // DOS header: "MZ" and the offset of the PE header at 0x3C.
        w.Write((ushort)0x5A4D);
        ms.Position = 0x3C;
        w.Write(0x40);

        // PE signature + COFF header.
        ms.Position = 0x40;
        w.Write(0x00004550u);
        w.Write((ushort)0x014C);   // Machine: i386
        w.Write((ushort)1);        // NumberOfSections
        w.Write(0u);               // TimeDateStamp
        w.Write(0u);               // PointerToSymbolTable
        w.Write(0u);               // NumberOfSymbols
        w.Write((ushort)224);      // SizeOfOptionalHeader
        w.Write((ushort)0x0102);   // Characteristics: executable, 32-bit

        // Optional header (PE32).
        w.Write((ushort)0x010B);   // Magic
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(0u);               // SizeOfCode
        w.Write((uint)rawSize);    // SizeOfInitializedData
        w.Write(0u);               // SizeOfUninitializedData
        w.Write(0u);               // AddressOfEntryPoint
        w.Write(0u);               // BaseOfCode
        w.Write((uint)SectionRva); // BaseOfData
        w.Write(0x400000u);        // ImageBase
        w.Write((uint)SectionRva); // SectionAlignment
        w.Write((uint)FileAlignment);
        w.Write((ushort)4); w.Write((ushort)0);  // OS version
        w.Write((ushort)0); w.Write((ushort)0);  // image version
        w.Write((ushort)4); w.Write((ushort)0);  // subsystem version
        w.Write(0u);               // Win32VersionValue
        w.Write((uint)(SectionRva + ((section.Length + 0xFFF) & ~0xFFF))); // SizeOfImage
        w.Write((uint)FileAlignment); // SizeOfHeaders
        w.Write(0u);               // CheckSum
        w.Write((ushort)3);        // Subsystem: console
        w.Write((ushort)0);        // DllCharacteristics
        w.Write(0x100000u); w.Write(0x1000u);    // stack reserve/commit
        w.Write(0x100000u); w.Write(0x1000u);    // heap reserve/commit
        w.Write(0u);               // LoaderFlags
        w.Write(16u);              // NumberOfRvaAndSizes
        for (var i = 0; i < 16; i++)
        {
            // Data directory 2 is the resource table.
            if (i == 2 && hasResources)
            {
                w.Write((uint)SectionRva);
                w.Write((uint)section.Length);
            }
            else
            {
                w.Write(0u);
                w.Write(0u);
            }
        }

        // Section header.
        w.Write(Encoding.ASCII.GetBytes(hasResources ? ".rsrc\0\0\0" : ".data\0\0\0"));
        w.Write((uint)section.Length);  // VirtualSize
        w.Write((uint)SectionRva);      // VirtualAddress
        w.Write((uint)rawSize);         // SizeOfRawData
        w.Write((uint)FileAlignment);   // PointerToRawData
        w.Write(0u); w.Write(0u);       // relocations / line numbers
        w.Write((ushort)0); w.Write((ushort)0);
        w.Write(0x40000040u);           // initialized data, readable

        ms.Position = FileAlignment;
        w.Write(section);
        return file;
    }

    /// <summary>Resource tree RT_VERSION(16) -> id 1 -> lang 0x409 -> VS_VERSION_INFO.</summary>
    private static byte[] BuildResourceSection(Version version)
    {
        var versionInfo = BuildVersionInfo(version);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        void Directory(uint idEntryOffsetOrData, uint id, bool subdirectory)
        {
            w.Write(0u);            // Characteristics
            w.Write(0u);            // TimeDateStamp
            w.Write((ushort)0);     // MajorVersion
            w.Write((ushort)0);     // MinorVersion
            w.Write((ushort)0);     // NumberOfNamedEntries
            w.Write((ushort)1);     // NumberOfIdEntries
            w.Write(id);
            w.Write(subdirectory ? 0x80000000u | idEntryOffsetOrData : idEntryOffsetOrData);
        }

        Directory(0x18, 16, subdirectory: true);     // root at 0x00 -> type directory at 0x18
        Directory(0x30, 1, subdirectory: true);      // name/id level at 0x18 -> language directory at 0x30
        Directory(0x48, 0x409, subdirectory: false); // language level at 0x30 -> data entry at 0x48

        w.Write((uint)(SectionRva + 0x58));          // data entry: RVA of the data
        w.Write((uint)versionInfo.Length);
        w.Write(0u);                                 // CodePage
        w.Write(0u);                                 // Reserved
        w.Write(versionInfo);
        return ms.ToArray();
    }

    private static byte[] BuildVersionInfo(Version v)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        const int fixedInfoSize = 52;
        var key = Encoding.Unicode.GetBytes("VS_VERSION_INFO\0");
        var headerSize = 6 + key.Length;
        var padding = (4 - headerSize % 4) % 4;

        w.Write((ushort)(headerSize + padding + fixedInfoSize)); // wLength
        w.Write((ushort)fixedInfoSize);                           // wValueLength
        w.Write((ushort)0);                                       // wType: binary
        w.Write(key);
        w.Write(new byte[padding]);

        var fileMs = ((uint)v.Major << 16) | (uint)v.Minor;
        var fileLs = ((uint)v.Build << 16) | (uint)v.Revision;
        w.Write(0xFEEF04BDu);   // dwSignature
        w.Write(0x00010000u);   // dwStrucVersion
        w.Write(fileMs); w.Write(fileLs);   // file version
        w.Write(fileMs); w.Write(fileLs);   // product version
        w.Write(0x3Fu);         // dwFileFlagsMask
        w.Write(0u);            // dwFileFlags
        w.Write(0x4u);          // dwFileOS: VOS_NT_WINDOWS32... (VOS__WINDOWS32)
        w.Write(0x1u);          // dwFileType: VFT_APP
        w.Write(0u);            // dwFileSubtype
        w.Write(0u); w.Write(0u); // dwFileDate
        return ms.ToArray();
    }
}
