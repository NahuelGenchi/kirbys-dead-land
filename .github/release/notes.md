<p align="center">
  <img src="$media/banner.png" alt="Kirby's Dead Land $version. Play as Kirby in Dead Cells on $platforms. Now with a launcher: no terminal needed." width="100%">
</p>

<!-- linux-only -->
<p align="center">
  <a href="$download/KirbysDeadLand-$version-x86_64.AppImage"><img src="$media/download.png" alt="Download for Linux" width="380"></a>
</p>

<p align="center">
  <b>$size</b> · for 64-bit Linux PCs<br>
  <sub>Prefer a normal folder? Get the <a href="$download/KirbysDeadLand-$version-x86_64.tar.gz">folder version (.tar.gz)</a> instead.</sub>
</p>
<!-- /linux-only -->
<!-- windows -->
<p align="center">
  <a href="$download/KirbysDeadLand-$version-x86_64.AppImage"><img src="$media/download.png" alt="Download for Linux" width="340"></a>
  &nbsp;
  <a href="$download/KirbysDeadLand-$version-windows-x64.zip"><img src="$media/download-windows.png" alt="Download for Windows" width="340"></a>
</p>

<p align="center">
  <b>Linux:</b> $size · <b>Windows:</b> $zip_size<br>
  <sub>Prefer a normal folder on Linux? Get the <a href="$download/KirbysDeadLand-$version-x86_64.tar.gz">folder version (.tar.gz)</a> instead.</sub>
</p>
<!-- /windows -->

$new

<!-- updating -->
## ⬆️ Already playing an older version?

<!-- linux-only -->
1. Download the new file above and allow it to run (right-click › **Properties** › **Allow running as a program**).
<!-- /linux-only -->
<!-- windows -->
1. Download the new version for your system above. On Linux, allow it to run (right-click › **Properties** › **Allow running as a program**); on Windows, unzip it.
<!-- /windows -->
2. Open it. It finds your existing setup, Kirby and saves by itself.
3. It updates its mod files in a few seconds. Then press **Play**.

Your Kirby saves and your normal Dead Cells saves are kept. You can delete the old version afterwards.
<!-- /updating -->

## 🌟 What is this?

Kirby's Dead Land lets you **play as Kirby inside Dead Cells**. Kirby walks, floats, inhales enemies and copies their powers. His moves, sprites and sounds come from **your own copy of Kirby Super Star Ultra**, which runs inside the mod. Dead Cells brings the levels, the enemies, the shields and your progress.

The launcher checks your PC, sets everything up once and starts the game. No terminal needed.

## ✅ What you need

- 💻 A 64-bit PC with $platforms_bold
- 🎮 **Steam** with **Dead Cells** installed (tested with version 35)
- ⭐ **Your own copy of Kirby Super Star Ultra (USA)** as a `.nds` file. It is not included here. The mod only reads it and never changes it.
- 💾 About **1 GB** of free disk space, and about **6 GB** of free memory while playing

## 🚀 From download to Kirby in 4 steps

### 1 · Open the launcher

<!-- linux-only -->
Download the file above. Right-click it, open **Properties**, and turn on **Allow running as a program** (some PCs call it *Executable as program*). Then double-click it.
<!-- /linux-only -->
<!-- windows -->
- 🐧 **Linux:** download the Linux file above. Right-click it, open **Properties**, and turn on **Allow running as a program** (some PCs call it *Executable as program*). Then double-click it.
- 🪟 **Windows:** download the Windows file above, right-click it and choose **Extract All**. Open the new folder and double-click **Kirby's Dead Land**. If Windows says it protected your PC, choose **More info**, then **Run anyway**.
<!-- /windows -->

### 2 · Let it check your PC

The launcher looks for Steam, Dead Cells, your Kirby game and a small safety helper. It usually finds your Kirby game by itself: just press **Use this file**.

<p align="center">
  <img src="$media/step-1-checks.png" alt="The launcher's welcome screen: Steam, Dead Cells and the safety helper have green ticks, and it found the Kirby game." width="88%">
</p>

If something is missing, it tells you exactly what to do, with a button to press or one command to copy. Fix it, then press **Check again**.

<p align="center">
  <img src="$media/step-2-fix.png" alt="An example with three problems: each one explains what to do, with a button or a command to copy." width="88%"><br>
  <sub>An example with three problems to fix.</sub>
</p>

### 3 · Set up Kirby, once

Press **Set up Kirby**. The launcher builds your Kirby from your own game files in about two minutes. You only do this once, and you can watch each step as it happens.

<p align="center">
  <img src="$media/step-3-setup.png" alt="The one-time setup: a progress ring and the list of setup steps." width="88%">
</p>

### 4 · Press Play 🎉

That's it. From now on, just open the launcher and press **Play**.

<p align="center">
  <img src="$media/step-4-play.png" alt="The Ready to play screen with a big Play button." width="88%"><br>
  <sub>These pictures use stand-in art. On your PC, the launcher shows your Kirby and his copy abilities, made from your own game.</sub>
</p>

## 🛡️ Your saves are safe

- Kirby runs use **their own saves**, so your normal Dead Cells progress stays as it is.
- Your normal saves are **backed up** every time you press Play.
- Nothing is installed into the Steam game folder, and your Kirby game file is never changed.

## 🎮 How Kirby plays

- The attack button **inhales**. Swallow an enemy to get a **random copy ability**, then the same button uses it.
- Kirby keeps a **Dead Cells shield** for parrying. Swords, bows and skills stay on the ground.
- Pause with **Escape** to read what your copy ability does.

Everything that has been checked in the game, and the known limits, are listed in the [README](https://github.com/$repo#readme).

## 🧪 What was tested

$tested

## 💬 Need help?

Something not working? [Open an issue](https://github.com/$repo/issues/new) and tell us what happened. In the launcher, **Settings › Last game log** shows the log you can attach.

<details>
<summary><b>📦 Files and licenses</b></summary>
<br>

| File | What it is |
| :--- | :--- |
| `KirbysDeadLand-$version-x86_64.AppImage` | The launcher, one file (recommended) |
| `KirbysDeadLand-$version-x86_64.tar.gz` | The same launcher as a folder. Unpack it and double-click `kirbys-dead-land`; `add-to-menu.sh` adds it to your applications menu. |
<!-- windows -->
| `KirbysDeadLand-$version-windows-x64.zip` | The launcher for Windows, as a folder. Extract it and double-click `Kirby's Dead Land`. |
<!-- /windows -->
| `kirbys-dead-land-$version.tar.gz` | Source code |
| `*.sha256` | Checksums to check the downloads |

Kirby's Dead Land is free software under the GNU GPL version 3. The launcher's **Settings › Licenses** (and `NOTICES.txt` and `NOTICES-emulator.txt` in each download$windows_notices) list the other software inside it.

Kirby Super Star Ultra and Dead Cells are not included. This project is not affiliated with or endorsed by Nintendo, HAL Laboratory, Motion Twin or Evil Empire.

</details>
