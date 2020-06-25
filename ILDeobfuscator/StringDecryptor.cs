using System;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace ILDeobfuscator
{
    public class StringDecryptor
        : IDeobfuscationPass
    {
        public void Run(ModuleDefMD module, Options opts)
        {
            if (opts.NoStringDecryption)
                return;

            if (opts.StringDecryptMethodToken == null || opts.StringDecryptMethodToken.Length == 0)
                throw new ArgumentException("StringDecryptMethodToken.Length == 0");

            int mdToken = Convert.ToInt32(opts.StringDecryptMethodToken, 16);
            PatchCallingAssemblyCheck(module, (uint)mdToken);
            module.Write(opts.OutputAssemblyPath + ".tmp");
            DecryptStringReferences(module, opts.OutputAssemblyPath + ".tmp", mdToken);
        }

        private void PatchCallingAssemblyCheck(ModuleDefMD module, uint mdToken)
        {
            MethodDef method = (MethodDef)module.ResolveToken(mdToken);

            for (int i = 0; i < method.Body.Instructions.Count - 1; i++)
            {
                if (method.Body.Instructions[i].OpCode.Code == Code.Call &&
                    method.Body.Instructions[i + 1].OpCode.Code == Code.Call &&
                    ((MemberRef)method.Body.Instructions[i].Operand).Name == "GetExecutingAssembly")
                {
                    method.Body.Instructions[i + 1].Operand = method.Body.Instructions[i].Operand;
                    break;
                }
            }
        }

        private void DecryptStringReferences(ModuleDefMD module, string assemblyPath, int mdToken)
        {
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            MethodBase decryptMethod = null;

            foreach (Module mod in assembly.GetModules())
            {
                decryptMethod = mod.ResolveMethod(mdToken);
                if (decryptMethod == null)
                    continue;

                if (decryptMethod.IsGenericMethod)
                    decryptMethod = ((MethodInfo)decryptMethod).MakeGenericMethod(new[] { typeof(string) });
            }

            if (decryptMethod == null)
                return;

            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;

                    for (int i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        Instruction callInsn = method.Body.Instructions[i];

                        if (callInsn.OpCode != OpCodes.Call)
                            continue;

                        if (callInsn.Operand is MethodSpec && ((MethodSpec)callInsn.Operand).Method.MDToken.Raw == mdToken)
                        {
                            Instruction stringIdInsn = method.Body.Instructions[i - 1];
                            uint stringId = (uint)(int)stringIdInsn.Operand;

                            string str = (string)decryptMethod.Invoke(null, new object[] { stringId });

                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
                            method.Body.Instructions[i - 1].Operand = null;
                            method.Body.Instructions[i].OpCode = OpCodes.Nop;
                            method.Body.Instructions[i].Operand = null;
                            method.Body.Instructions.Insert(i + 1, Instruction.Create(OpCodes.Ldstr, str));
                        }

                        if (callInsn.Operand is MethodDef && ((MethodDef)callInsn.Operand).MDToken.Raw == mdToken)
                        {
                            Instruction stringIdInsn = method.Body.Instructions[i - 1];
                            int stringId = (int)stringIdInsn.Operand;

                            string str = (string)decryptMethod.Invoke(null, new object[] { stringId });

                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
                            method.Body.Instructions[i - 1].Operand = null;
                            method.Body.Instructions[i].OpCode = OpCodes.Nop;
                            method.Body.Instructions[i].Operand = null;
                            method.Body.Instructions.Insert(i + 1, Instruction.Create(OpCodes.Ldstr, str));
                        }
                    }

                    method.Body.SimplifyMacros(method.Parameters);
                    method.Body.OptimizeMacros();
                }
            }
        }
    }
}
