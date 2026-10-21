# CMake toolchain file for melon-rs's melonDS build when scripts/build_packages.py
# makes a player package. It only keeps link-time optimization off, as in the lab's
# verified builds: with it on, GCC writes bytecode objects that Rust's linker cannot read.
set(ENABLE_LTO_RELEASE OFF CACHE BOOL "Enable link-time optimizations for release builds" FORCE)
