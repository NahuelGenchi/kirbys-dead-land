"""Where Steam and its Dead Cells are on this system, when no option names them (#246).

On Linux, Steam is ~/.local/share/Steam. On Windows it is the folder the Steam
client records in the registry (HKCU\\Software\\Valve\\Steam, SteamPath), or the
installer's default folder when that is missing. The game is the main library's
Dead Cells; a game in another library is named with --game.
"""
import os
from pathlib import Path


def steam_root() -> Path:
    if os.name != 'nt':
        return Path.home() / '.local/share/Steam'
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam') as key:
            return Path(winreg.QueryValueEx(key, 'SteamPath')[0])
    except OSError:
        return Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Steam'


def default_game() -> Path:
    return steam_root() / 'steamapps/common/Dead Cells'
