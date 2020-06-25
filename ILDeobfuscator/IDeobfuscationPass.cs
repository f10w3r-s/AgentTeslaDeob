using System;
using dnlib.DotNet;

namespace ILDeobfuscator
{
    public interface IDeobfuscationPass
    {
        void Run(ModuleDefMD module, Options opts);
    }
}
