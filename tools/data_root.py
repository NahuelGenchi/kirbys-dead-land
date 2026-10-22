"""The private data root every script and tool writes beneath.

`KIRBY_DATA_ROOT` moves it, for example to keep the player launcher's files apart
from a development lab; unset, it is ~/.local/share/kirby-dead-cells on Linux and
%LOCALAPPDATA%\\kirby-dead-cells on Windows (#246).
"""
import os
from pathlib import Path


def data_root() -> Path:
    override = os.environ.get('KIRBY_DATA_ROOT')
    if override:
        return Path(override).expanduser().resolve()
    if os.name == 'nt':
        return Path(os.environ.get('LOCALAPPDATA') or Path.home() / 'AppData/Local') / 'kirby-dead-cells'
    return Path.home() / '.local/share/kirby-dead-cells'
