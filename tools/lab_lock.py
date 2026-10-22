"""The private lab's lock, held while a build is staged or the game runs.

It is an exclusive lock on the first byte of <data root>/lab.runtime.lock, taken
without waiting: `flock` on Linux and `msvcrt.locking` on Windows (#246). It is
released when the file closes or the process ends.
"""
import os
from pathlib import Path
from typing import IO

if os.name == 'nt':
    import msvcrt
    LOCKED = OSError  # what msvcrt.locking raises for a byte another process holds
else:
    import fcntl
    LOCKED = BlockingIOError


def hold(path: Path) -> IO[str] | None:
    """The open, locked file, or None while another process holds the lock."""
    stream = path.open('a')
    try:
        if os.name == 'nt':
            # Append mode starts at the end; every process locks byte 0.
            os.lseek(stream.fileno(), 0, os.SEEK_SET)
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except LOCKED:
        stream.close()
        return None
    return stream
