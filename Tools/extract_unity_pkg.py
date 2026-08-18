#!/usr/bin/env python3
"""Extract the payload of an official Unity macOS-style .pkg on Linux.

Some Unity 2022 Linux release manifests publish Android Build Support as a
XAR/PKG archive. Its payload is platform-independent AndroidPlayer content,
stored as a gzip-compressed portable-ASCII cpio stream. This extractor avoids
third-party packages and rejects unsafe archive paths.
"""

from __future__ import annotations

import argparse
import gzip
import os
import shutil
import stat
import struct
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path, PurePosixPath


XAR_HEADER = struct.Struct(">4sHHQQI")
CPIO_HEADER_SIZE = 76


def read_exact(stream, size: int) -> bytes:
    chunks: list[bytes] = []
    remaining = size
    while remaining:
        chunk = stream.read(min(1024 * 1024, remaining))
        if not chunk:
            raise EOFError(f"archive ended with {remaining} bytes missing")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def safe_output_path(root: Path, archive_name: str) -> Path:
    normalized = archive_name.removeprefix("./")
    relative = PurePosixPath(normalized)
    if not normalized or normalized == ".":
        return root
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"unsafe archive path: {archive_name!r}")
    return root.joinpath(*relative.parts)


def copy_exact(source, destination, size: int) -> None:
    remaining = size
    while remaining:
        chunk = source.read(min(1024 * 1024, remaining))
        if not chunk:
            raise EOFError(f"cpio payload ended with {remaining} bytes missing")
        destination.write(chunk)
        remaining -= len(chunk)


def extract_odc_cpio(stream, destination: Path) -> int:
    destination.mkdir(parents=True, exist_ok=True)
    extracted = 0
    while True:
        header = stream.read(CPIO_HEADER_SIZE)
        if not header:
            raise EOFError("cpio archive has no TRAILER!!! record")
        if len(header) != CPIO_HEADER_SIZE or header[:6] != b"070707":
            raise ValueError("unsupported or corrupt cpio header")

        mode = int(header[18:24], 8)
        modified = int(header[48:59], 8)
        name_size = int(header[59:65], 8)
        file_size = int(header[65:76], 8)
        raw_name = read_exact(stream, name_size)
        name = raw_name.rstrip(b"\0").decode("utf-8")
        if name == "TRAILER!!!":
            break

        output = safe_output_path(destination, name)
        if output == destination:
            if file_size:
                read_exact(stream, file_size)
            continue

        output.parent.mkdir(parents=True, exist_ok=True)
        file_type = stat.S_IFMT(mode)
        permissions = stat.S_IMODE(mode)

        if file_type == stat.S_IFDIR:
            output.mkdir(parents=True, exist_ok=True)
            if file_size:
                read_exact(stream, file_size)
        elif file_type == stat.S_IFLNK:
            target = read_exact(stream, file_size).decode("utf-8")
            if output.exists() or output.is_symlink():
                output.unlink()
            output.symlink_to(target)
        elif file_type == stat.S_IFREG:
            with output.open("wb") as handle:
                copy_exact(stream, handle, file_size)
            os.chmod(output, permissions)
            os.utime(output, (modified, modified), follow_symlinks=False)
        else:
            read_exact(stream, file_size)
            raise ValueError(f"unsupported cpio entry type for {name!r}: {oct(file_type)}")
        extracted += 1
    return extracted


def payload_location(package: Path) -> tuple[int, int]:
    with package.open("rb") as handle:
        header = read_exact(handle, XAR_HEADER.size)
        magic, header_size, version, compressed_size, _, _ = XAR_HEADER.unpack(header)
        if magic != b"xar!" or version != 1 or header_size < XAR_HEADER.size:
            raise ValueError("not a supported XAR package")
        handle.seek(header_size)
        toc = zlib.decompress(read_exact(handle, compressed_size))

    root = ET.fromstring(toc)
    for entry in root.findall(".//file"):
        if entry.findtext("name") == "Payload":
            offset = int(entry.findtext("data/offset", "-1"))
            length = int(entry.findtext("data/length", "-1"))
            if offset < 0 or length <= 0:
                break
            return header_size + compressed_size + offset, length
    raise ValueError("XAR package does not contain a Payload entry")


def extract_package(package: Path, destination: Path) -> int:
    offset, _ = payload_location(package)
    with package.open("rb") as handle:
        handle.seek(offset)
        with gzip.GzipFile(fileobj=handle, mode="rb") as payload:
            return extract_odc_cpio(payload, destination)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    count = extract_package(args.package.resolve(), args.destination.resolve())
    print(f"Extracted {count} AndroidPlayer entries to {args.destination}")


if __name__ == "__main__":
    main()
