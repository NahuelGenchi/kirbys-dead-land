"""Checked scanout-layer capture additions and GPU initialization fix.

The capture retains the two actual priority-selected RGB6 pixels and the window /
color-effect registers at each scanline, before final source composition. It is
diagnostic source data, not a reconstruction from end-of-frame GPU registers.
The pinned core also needs its initial capture flags zeroed before SetRenderer:
its constructor otherwise reads uninitialized flags and can call a null renderer.

A second stage (#123) serves the live runtime, which shows neither DS screen and
reads only engine A: ``MainScreenOnly`` leaves engine B's 2D drawing out (its
half of the framebuffer is filled black), and ``Copy_ObjVram`` copies engine A's
OBJ VRAM eight bytes at a time through the same accessors as the ARM9 bus read.
The switch is off unless the bridge is asked; neither changes emulated state.

A third stage (#148) leaves engine A undrawn as well where the host would learn
nothing from it. The host reads that drawing only through the layer capture, and
there only to find a palette or colour effect over the whole frame. With
``SkipIdleMain`` on, a scanline of an enabled, unblanked unit with no colour
effect control whose standard BG palette equals ``IdlePalette`` is not drawn:
the capture of such a line reads as "no effect". ``MainFrame`` says whether the
frame that ended at the last VBlank was drawn (0), left out (1) or mixed (2).
A scanline a display capture reads is always drawn, and sprites are drawn for
every scanline at their usual time, so emulated state is the same either way.
Off unless the bridge is asked.
"""
import subprocess
from pathlib import Path

WRAPPER_EDITS = {
    "src/melon/cpp/Shims.h": [(
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index);",
        "    bool Copy_LayerCapture(NDS &nds, u32 *dest, u32 plane, u32 count);\n"
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index);",
    )],
    "src/melon/cpp/Shims.cpp": [
        ('#include "GPU.h"', '#include "GPU.h"\n#include "GPU_Soft.h"'),
        ("    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n",
         "    bool Copy_LayerCapture(NDS &nds, u32 *dest, u32 plane, u32 count)\n"
         "    {\n"
         "        auto* renderer = dynamic_cast<SoftRenderer*>(&nds.GPU.GetRenderer());\n"
         "        return renderer && renderer->CopyLayerCapture(dest, plane, count);\n"
         "    }\n\n"
         "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n"),
    ],
}

CORE_EDITS = {
    "src/GPU.h": [(
        "    u16 VRAMCaptureBlockFlags[16];",
        "    u16 VRAMCaptureBlockFlags[16] {};",
    )],
    "src/GPU2D_Soft.h": [
        ("    void DrawScanline(u32 line) override;", "    bool CopyLayerCapture(u32* dest, u32 plane, u32 count) const;\n\n    void DrawScanline(u32 line) override;"),
        ("    SoftRenderer& Parent;", "    SoftRenderer& Parent;\n\n    u32 LayerCapture[5][256*192] {};"),
    ],
    "src/GPU2D_Soft.cpp": [
        ("    memset(BGOBJLine, 0, sizeof(BGOBJLine));", "    memset(LayerCapture, 0, sizeof(LayerCapture));\n    memset(BGOBJLine, 0, sizeof(BGOBJLine));"),
        ("u32 SoftRenderer2D::ColorComposite(int i, u32 val1, u32 val2) const\n", "bool SoftRenderer2D::CopyLayerCapture(u32* dest, u32 plane, u32 count) const\n{\n    if (!dest || plane >= 5 || count < 256*192) return false;\n    memcpy(dest, LayerCapture[plane], sizeof(LayerCapture[plane]));\n    return true;\n}\n\nu32 SoftRenderer2D::ColorComposite(int i, u32 val1, u32 val2) const\n"),
        ("    u32* dst = Parent.Output2D[GPU2D.Num];", "    u32* dst = Parent.Output2D[GPU2D.Num];\n\n    if (line < 192)\n        for (u32 plane = 0; plane < 5; ++plane)\n            memset(&LayerCapture[plane][line*256], 0, 256*sizeof(u32));"),
        ("        dst[i] = ColorComposite(i, val1, val2);", "        dst[i] = ColorComposite(i, val1, val2);\n        if (line < 192)\n        {\n            const u32 at = line*256+i;\n            LayerCapture[0][at] = val1;\n            LayerCapture[1][at] = val2;\n            LayerCapture[2][at] = 0x80000000 | (GPU2D.EVY << 24) | (GPU2D.BlendCnt << 8) | WindowMask[i];\n            LayerCapture[3][at] = dst[i];\n            LayerCapture[4][at] = reinterpret_cast<u16*>(&GPU.Palette[GPU2D.Num ? 0x400 : 0])[i];\n        }"),
    ],
    "src/GPU_Soft.h": [(
        "    bool GetFramebuffers(void** top, void** bottom) override;",
        "    bool GetFramebuffers(void** top, void** bottom) override;\n    bool CopyLayerCapture(u32* dest, u32 plane, u32 count) const;",
    )],
    "src/GPU_Soft.cpp": [(
        "bool SoftRenderer::GetFramebuffers(void** top, void** bottom)\n",
        "bool SoftRenderer::CopyLayerCapture(u32* dest, u32 plane, u32 count) const\n{\n    const auto* renderer = dynamic_cast<const SoftRenderer2D*>(Rend2D_A.get());\n    return renderer && renderer->CopyLayerCapture(dest, plane, count);\n}\n\nbool SoftRenderer::GetFramebuffers(void** top, void** bottom)\n",
    )],
}

# Second stage (#123), applied on top of the edits above.
WRAPPER_EDITS_2 = {
    "src/melon/cpp/Shims.h": [(
        "    bool Copy_LayerCapture(NDS &nds, u32 *dest, u32 plane, u32 count);\n",
        "    bool Copy_LayerCapture(NDS &nds, u32 *dest, u32 plane, u32 count);\n"
        "    bool Copy_ObjVram(NDS &nds, u8 *dest, u32 offset, u32 count);\n"
        "    bool Set_MainScreenOnly(NDS &nds, bool enabled);\n",
    )],
    "src/melon/cpp/Shims.cpp": [(
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n",
        "    bool Copy_ObjVram(NDS &nds, u8 *dest, u32 offset, u32 count)\n"
        "    {\n"
        "        // Engine A OBJ VRAM as ARM9Read8 returns it at 0x06400000 + offset.\n"
        "        if (!dest || (offset & 7) || (count & 7) || offset > 0x40000 || count > 0x40000 - offset) return false;\n"
        "        for (u32 at = offset; at < offset + count; at += 8)\n"
        "        {\n"
        "            if (at == offset || !(at & 0x3FFF)) nds.GPU.SyncVRAM_AOBJ(at, false);\n"
        "            const u64 value = nds.GPU.ReadVRAM_AOBJ<u64>(at);\n"
        "            memcpy(dest + (at - offset), &value, sizeof(value));\n"
        "        }\n"
        "        return true;\n"
        "    }\n\n"
        "    bool Set_MainScreenOnly(NDS &nds, bool enabled)\n"
        "    {\n"
        "        auto* renderer = dynamic_cast<SoftRenderer*>(&nds.GPU.GetRenderer());\n"
        "        if (!renderer) return false;\n"
        "        renderer->MainScreenOnly = enabled;\n"
        "        return true;\n"
        "    }\n\n"
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n",
    )],
}

CORE_EDITS_2 = {
    "src/GPU_Soft.h": [(
        "    bool CopyLayerCapture(u32* dest, u32 plane, u32 count) const;\n",
        "    bool CopyLayerCapture(u32* dest, u32 plane, u32 count) const;\n"
        "    // Checked addition: engine B's 2D output is not drawn; its framebuffer half is black.\n"
        "    bool MainScreenOnly = false;\n",
    )],
    "src/GPU_Soft.cpp": [
        ("        Rend2D_B->DrawScanline(line);\n", "        if (!MainScreenOnly) Rend2D_B->DrawScanline(line);\n"),
        ("        DrawScanlineB(line, dstB);\n", "        if (!MainScreenOnly) DrawScanlineB(line, dstB);\n"),
        ("        ExpandColor(dstB);\n", "        if (!MainScreenOnly) ExpandColor(dstB);\n"),
        ("            dstA[i] = 0xFF000000;\n            dstB[i] = 0xFF000000;\n        }\n    }\n}\n",
         "            dstA[i] = 0xFF000000;\n            dstB[i] = 0xFF000000;\n        }\n    }\n\n"
         "    if (MainScreenOnly)\n        for (int i = 0; i < 256; i++)\n            dstB[i] = 0xFF000000;\n}\n"),
        ("    Rend2D_B->DrawSprites(line);\n", "    if (!MainScreenOnly) Rend2D_B->DrawSprites(line);\n"),
    ],
}

# Third stage (#148), applied on top of the second.
WRAPPER_EDITS_3 = {
    "src/melon/cpp/Shims.h": [(
        "    bool Set_MainScreenOnly(NDS &nds, bool enabled);\n",
        "    bool Set_MainScreenOnly(NDS &nds, bool enabled);\n"
        "    bool Set_SkipIdleMain(NDS &nds, const u8 *palette);\n"
        "    u32 Get_MainFrame(NDS &nds);\n",
    )],
    "src/melon/cpp/Shims.cpp": [(
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n",
        "    bool Set_SkipIdleMain(NDS &nds, const u8 *palette)\n"
        "    {\n"
        "        // palette: the 512 bytes of standard BG palette an idle scanline has, or null for off.\n"
        "        auto* renderer = dynamic_cast<SoftRenderer*>(&nds.GPU.GetRenderer());\n"
        "        if (!renderer) return false;\n"
        "        renderer->SkipIdleMain = palette != nullptr;\n"
        "        if (palette) memcpy(renderer->IdlePalette, palette, sizeof(renderer->IdlePalette));\n"
        "        return true;\n"
        "    }\n\n"
        "    u32 Get_MainFrame(NDS &nds)\n"
        "    {\n"
        "        auto* renderer = dynamic_cast<SoftRenderer*>(&nds.GPU.GetRenderer());\n"
        "        return renderer ? renderer->MainFrame : 0;\n"
        "    }\n\n"
        "    bool Copy_Framebuffers(NDS &nds, u8 *dest, bool index)\n",
    )],
}

CORE_EDITS_3 = {
    "src/GPU_Soft.h": [
        ("    void VBlank() override {};\n",
         "    void VBlank() override\n"
         "    {\n"
         "        MainFrame = !MainSkipped ? 0 : (MainSkipped == 192 && !MainDrawn) ? 1 : 2;\n"
         "        MainSkipped = MainDrawn = 0;\n"
         "    };\n"),
        ("    bool MainScreenOnly = false;\n",
         "    bool MainScreenOnly = false;\n"
         "    // Checked addition: the BG and composition of an engine A scanline that MainIdle\n"
         "    // accepts are not drawn (its sprites still are, one scanline ahead as always).\n"
         "    // MainFrame is the frame that ended at the last VBlank: 0 every scanline drawn,\n"
         "    // 1 none (192 left out), 2 some of each.\n"
         "    bool SkipIdleMain = false;\n"
         "    u8 IdlePalette[0x200] {};\n"
         "    u32 MainFrame = 0;\n"),
        ("    void DoCapture(u32 line);\n",
         "    void DoCapture(u32 line);\n\n"
         "    bool MainIdle() const;\n"
         "    void SyncMain();\n"
         "    u32 MainSkipped = 0, MainDrawn = 0;\n"
         "    bool MainStale = false; // engine A's flat BG VRAM copies missed scanlines\n"),
    ],
    "src/GPU_Soft.cpp": [
        ("void SoftRenderer::DrawScanline(u32 line)\n",
         "bool SoftRenderer::MainIdle() const\n"
         "{\n"
         "    // The layer capture of such a scanline reads as \"no effect\": every pixel valid,\n"
         "    // no colour effect control, the palette the host compares against. A display\n"
         "    // capture reads the drawn line, so its scanlines are never idle.\n"
         "    return SkipIdleMain && !GPU.CaptureEnable && GPU.GPU2D_A.Enabled && !GPU.GPU2D_A.ForcedBlank\n"
         "        && !GPU.GPU2D_A.BlendCnt && !memcmp(GPU.Palette, IdlePalette, sizeof(IdlePalette));\n"
         "}\n\n"
         "void SoftRenderer::SyncMain()\n"
         "{\n"
         "    // The BG dirty tracking was not followed while no scanline was drawn: copy all of\n"
         "    // it again. Sprites are drawn for every scanline, so OBJ VRAM was followed.\n"
         "    if (!MainStale) return;\n"
         "    GPU.VRAMDirty_ABG.Reset();\n"
         "    GPU.VRAMDirty_ABGExtPal.Reset();\n"
         "    GPU.VRAMDirty_AOBJExtPal.Reset();\n"
         "    MainStale = false;\n"
         "}\n\n"
         "void SoftRenderer::DrawScanline(u32 line)\n"),
        ("    line = GPU.VCount;\n    if (line < 192)\n",
         "    line = GPU.VCount;\n    bool idle = false;\n    if (line < 192)\n"),
        ("        // draw BG/OBJ layers\n        Rend2D_A->DrawScanline(line);\n",
         "        idle = MainIdle();\n"
         "        if (idle)\n"
         "        {\n"
         "            MainSkipped++;\n"
         "            MainStale = true;\n"
         "        }\n"
         "        else\n"
         "        {\n"
         "            MainDrawn++;\n"
         "            SyncMain();\n"
         "        }\n\n"
         "        // draw BG/OBJ layers\n        if (!idle) Rend2D_A->DrawScanline(line);\n"),
        ("        DrawScanlineA(line, dstA);\n", "        if (!idle) DrawScanlineA(line, dstA);\n"),
        ("        ExpandColor(dstA);\n", "        if (!idle) ExpandColor(dstA);\n"),
        ("    if (MainScreenOnly)\n        for (int i = 0; i < 256; i++)\n            dstB[i] = 0xFF000000;\n}\n",
         "    if (MainScreenOnly)\n        for (int i = 0; i < 256; i++)\n            dstB[i] = 0xFF000000;\n\n"
         "    if (idle)\n        for (int i = 0; i < 256; i++)\n            dstA[i] = 0xFF000000;\n}\n"),
    ],
}

GRAPHICS_WRAPPER_FILES = tuple(WRAPPER_EDITS)
GRAPHICS_CORE_FILES = tuple(CORE_EDITS)


def check_graphics_patch(vendor: Path, wrapper_revision: str, core_revision: str, apply=False):
    """Accept exactly pristine or a known stage of this patch; apply only after all checks."""
    writes = []
    for root, revision, edits, second, third in ((vendor, wrapper_revision, WRAPPER_EDITS, WRAPPER_EDITS_2, WRAPPER_EDITS_3),
                                                 (vendor / "melonDS", core_revision, CORE_EDITS, CORE_EDITS_2, CORE_EDITS_3)):
        for relative, replacements in edits.items():
            original = subprocess.check_output(["git", "-C", str(root), "show", f"{revision}:{relative}"], text=True)
            first = original
            for before, after in replacements:
                if first.count(before) != 1:
                    raise SystemExit(f"Graphics capture patch precondition failed: {relative}")
                first = first.replace(before, after)
            staged = first
            for before, after in second.get(relative, []):
                if staged.count(before) != 1:
                    raise SystemExit(f"Graphics patch second-stage precondition failed: {relative}")
                staged = staged.replace(before, after)
            patched = staged
            for before, after in third.get(relative, []):
                if patched.count(before) != 1:
                    raise SystemExit(f"Graphics patch third-stage precondition failed: {relative}")
                patched = patched.replace(before, after)
            target = root / relative
            actual = target.read_text()
            legacy = first.replace("LayerCapture[5]", "LayerCapture[4]").replace("plane >= 5", "plane >= 4").replace("plane < 5", "plane < 4").replace("            LayerCapture[4][at] = reinterpret_cast<u16*>(&GPU.Palette[GPU2D.Num ? 0x400 : 0])[i];\n", "")
            if actual not in (original, legacy, first, staged, patched):
                raise SystemExit(f"Unexpected graphics capture edits: {relative}; left untouched.")
            if actual != patched:
                writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
