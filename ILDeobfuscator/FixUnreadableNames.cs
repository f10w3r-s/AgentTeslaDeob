using System;
using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace ILDeobfuscator
{
    public class FixUnreadableNames
        : IDeobfuscationPass
    {
        public void Run(ModuleDefMD module, Options opts)
        {
            if (opts.NoRename)
                return;

            Regex validRegex = new Regex(opts.ValidNameRegex);

            int classIndex = 0;
            int structIndex = 0;
            int enumIndex = 0;
            int interfaceIndex = 0;

            foreach (TypeDef type in module.GetTypes())
            {
                int staticMethodIndex = 0;
                int methodIndex = 0;
                int staticFieldIndex = 0;
                int fieldIndex = 0;

                if (!validRegex.IsMatch(type.Name))
                {
                    if (type.IsClass && !type.IsValueType)
                        type.Name = "Class" + ++classIndex;
                    else if (type.IsClass)
                        type.Name = "Struct" + ++structIndex;
                    else if (type.IsEnum)
                        type.Name = "Enum" + ++enumIndex;
                    else if (type.IsInterface)
                        type.Name = "Interface" + ++interfaceIndex;
                }

                foreach (MethodDef method in type.Methods)
                {
                    if (!validRegex.IsMatch(method.Name))
                    {
                        if (method.IsStatic)
                            method.Name = "StaticMethod" + ++staticMethodIndex;
                        else
                            method.Name = "Method" + ++methodIndex;
                    }
                }

                foreach (FieldDef field in type.Fields)
                {
                    if (!validRegex.IsMatch(field.Name))
                    {
                        if (field.IsStatic)
                            field.Name = "StaticField" + ++staticFieldIndex;
                        else
                            field.Name = "Field" + ++fieldIndex;
                    }
                }
            }
        }
    }
}
