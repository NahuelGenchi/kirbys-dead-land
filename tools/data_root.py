"""The private data root every script and tool writes beneath.

`KIRBY_DATA_ROOT` moves it, for example to keep the player launcher's files apart
from a development lab; unset, it is ~/.local/share/kirby-dead-cells.
"""
import os
from pathlib import Path


def data_root() -> Path:
    override = os.environ.get('KIRBY_DATA_ROOT')
    return Path(override).expanduser().resolve() if override else Path.home() / '.local/share/kirby-dead-cells'
