#!/usr/bin/env python3
"""Writes a tiny, valid 32-bit PE file whose version resource says <major.minor.build.revision>.

Usage: make_fake_wow_exe.py <output-file> <version, e.g. 3.3.5.12340>
It is a port of tests/RealmPortal.Tests/Support/SyntheticPe.cs. The file is only parsed, never executed.
"""
import struct
import sys

FILE_ALIGN = 0x200
SECTION_RVA = 0x1000


def version_info(major, minor, build, rev):
    key = "VS_VERSION_INFO\0".encode("utf-16-le")
    header = 6 + len(key)
    pad = (4 - header % 4) % 4
    ms = (major << 16) | minor
    ls = (build << 16) | rev
    out = struct.pack("<HHH", header + pad + 52, 52, 0) + key + b"\0" * pad
    out += struct.pack("<13I", 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 4, 1, 0, 0, 0)
    return out


def resource_section(vi):
    def directory(target, ident, sub):
        return struct.pack("<IIHHHHII", 0, 0, 0, 0, 0, 1, ident, (0x80000000 | target) if sub else target)

    out = directory(0x18, 16, True) + directory(0x30, 1, True) + directory(0x48, 0x409, False)
    out += struct.pack("<IIII", SECTION_RVA + 0x58, len(vi), 0, 0) + vi
    return out


def build_pe(section):
    raw = (len(section) + FILE_ALIGN - 1) // FILE_ALIGN * FILE_ALIGN
    f = bytearray(FILE_ALIGN + raw)
    f[0:2] = b"MZ"
    struct.pack_into("<I", f, 0x3C, 0x40)
    pos = 0x40
    hdr = struct.pack("<IHHIIIHH", 0x00004550, 0x014C, 1, 0, 0, 0, 224, 0x0102)
    f[pos:pos + len(hdr)] = hdr
    pos += len(hdr)
    size_of_image = SECTION_RVA + ((len(section) + 0xFFF) & ~0xFFF)
    opt = struct.pack("<HBBIIIIIIIIIHHHHHHIIIIHHIIIIII",
                      0x010B, 0, 0, 0, raw, 0, 0, 0, SECTION_RVA, 0x400000, SECTION_RVA, FILE_ALIGN,
                      4, 0, 0, 0, 4, 0, 0, size_of_image, FILE_ALIGN, 0, 3, 0,
                      0x100000, 0x1000, 0x100000, 0x1000, 0, 16)
    f[pos:pos + len(opt)] = opt
    pos += len(opt)
    for i in range(16):
        d = struct.pack("<II", SECTION_RVA, len(section)) if i == 2 else struct.pack("<II", 0, 0)
        f[pos:pos + 8] = d
        pos += 8
    sec = b".rsrc\0\0\0" + struct.pack("<IIIIIIHHI", len(section), SECTION_RVA, raw, FILE_ALIGN, 0, 0, 0, 0, 0x40000040)
    f[pos:pos + len(sec)] = sec
    f[FILE_ALIGN:FILE_ALIGN + len(section)] = section
    return bytes(f)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    parts = [int(p) for p in sys.argv[2].split(".")]
    with open(sys.argv[1], "wb") as out:
        out.write(build_pe(resource_section(version_info(*parts))))
