"""Read-only x64 disassembly of a PE address range (requires pefile, capstone)."""

import argparse
import struct

import capstone
import pefile


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("binary")
parser.add_argument("address", type=lambda value: int(value, 0))
parser.add_argument("size", nargs="?", type=lambda value: int(value, 0), default=0x200)
parser.add_argument("--callers", action="store_true")
parser.add_argument("--references", action="store_true",
                    help="Find absolute pointers and candidate rel32 references")
parser.add_argument("--function", action="store_true",
                    help="Disassemble the containing PE exception-table function")
args = parser.parse_args()

pe = pefile.PE(args.binary, fast_load=True)
if args.references:
    pointer = struct.pack("<Q", args.address)
    for section in pe.sections:
        data = section.get_data()
        base = pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress
        offset = data.find(pointer)
        while offset >= 0:
            print(f"{base + offset:#x} absolute pointer")
            offset = data.find(pointer, offset + 1)
        if section.Characteristics & 0x20000000:
            for offset in range(len(data) - 3):
                target = base + offset + 4 + struct.unpack_from("<i", data, offset)[0]
                if target == args.address:
                    print(f"{base + offset:#x} rel32 displacement (verify instruction)")
    raise SystemExit(0)
if args.function:
    pe.parse_data_directories(directories=[
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    rva = args.address - pe.OPTIONAL_HEADER.ImageBase
    for entry in pe.DIRECTORY_ENTRY_EXCEPTION:
        if entry.struct.BeginAddress <= rva < entry.struct.EndAddress:
            args.address = pe.OPTIONAL_HEADER.ImageBase + entry.struct.BeginAddress
            args.size = entry.struct.EndAddress - entry.struct.BeginAddress
            print(f"Function {args.address:#x}, size {args.size:#x}")
            break
    else:
        raise SystemExit("No containing exception-table function found")
if args.callers:
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        data = section.get_data()
        base = pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress
        for offset in range(len(data) - 4):
            if data[offset] == 0xE8:
                target = base + offset + 5 + struct.unpack_from("<i", data, offset + 1)[0]
                if target == args.address:
                    print(f"{base + offset:#x} call {target:#x}")
    raise SystemExit(0)

code = pe.get_data(args.address - pe.OPTIONAL_HEADER.ImageBase, args.size)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for instruction in decoder.disasm(code, args.address):
    print(f"{instruction.address:#x}  {instruction.bytes.hex():24}  "
          f"{instruction.mnemonic:8} {instruction.op_str}")
