# Seed map engine

The C shim uses [Cubiomes](https://github.com/Cubitect/cubiomes) at commit
`e61f90580cbdd883214a8054670dacae655e59c0`. The unmodified upstream sources
needed by the shim are in `cubiomes/`, with their MIT license.

The launcher builds the engine with the Visual Studio C++ x64 build tools on
Windows and with the system C compiler on Linux and macOS.
