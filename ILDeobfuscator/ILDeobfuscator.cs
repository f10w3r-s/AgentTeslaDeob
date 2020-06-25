using System;
using System.Collections.Generic;
using dnlib.DotNet;

namespace ILDeobfuscator
{
    public class ILDeobfuscator
    {
        private Options opts;
        private List<IDeobfuscationPass> passes = new List<IDeobfuscationPass>();

        public ILDeobfuscator(Options opts)
        {
            this.opts = opts;

            passes.Add(new CFGDeobfuscator());
            passes.Add(new StringDecryptor());
            passes.Add(new FixUnreadableNames());
        }

        public void Run()
        {
            ModuleContext context = ModuleDef.CreateModuleContext();
            ModuleDefMD module = ModuleDefMD.Load(opts.InputAssemblyPath, context);

            foreach (IDeobfuscationPass pass in passes)
                pass.Run(module, opts);

            module.Write(opts.OutputAssemblyPath);
        }
    }
}
