"""Checked scanout-layer capture additions and GPU initialization fix.

The capture retains the two actual priority-selected RGB6 pixels and the window /
color-effect registers at each scanline, before final source composition. It is
diagnostic source data, not a reconstruction from end-of-frame GPU registers.
The pinned core also needs its initial capture flags zeroed before SetRenderer:
its constructor otherwise reads uninitialized flags and can call a null renderer.
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

GRAPHICS_WRAPPER_FILES = tuple(WRAPPER_EDITS)
GRAPHICS_CORE_FILES = tuple(CORE_EDITS)


def check_graphics_patch(vendor: Path, wrapper_revision: str, core_revision: str, apply=False):
    """Accept exactly pristine or this known patch; apply only after all checks."""
    writes = []
    for root, revision, edits in ((vendor, wrapper_revision, WRAPPER_EDITS),
                                  (vendor / "melonDS", core_revision, CORE_EDITS)):
        for relative, replacements in edits.items():
            original = subprocess.check_output(["git", "-C", str(root), "show", f"{revision}:{relative}"], text=True)
            patched = original
            for before, after in replacements:
                if patched.count(before) != 1:
                    raise SystemExit(f"Graphics capture patch precondition failed: {relative}")
                patched = patched.replace(before, after)
            target = root / relative
            actual = target.read_text()
            legacy = patched.replace("LayerCapture[5]", "LayerCapture[4]").replace("plane >= 5", "plane >= 4").replace("plane < 5", "plane < 4").replace("            LayerCapture[4][at] = reinterpret_cast<u16*>(&GPU.Palette[GPU2D.Num ? 0x400 : 0])[i];\n", "")
            if actual not in (original, legacy, patched):
                raise SystemExit(f"Unexpected graphics capture edits: {relative}; left untouched.")
            if actual != patched:
                writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
