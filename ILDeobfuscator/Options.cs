using System;
using CommandLine;

namespace ILDeobfuscator
{
    public class Options
    {
        [Option('i', "in", Required = true, HelpText = "Input assembly path")]
        public string InputAssemblyPath { get; set; }
        [Option('o', "out", Required = true, HelpText = "Output assembly path")]
        public string OutputAssemblyPath { get; set; }

        [Option("no-rename", Default = false)]
        public bool NoRename { get; set; }
        [Option("valid-name-regex", Default = @"^[a-zA-Z0-9_\.]+$")]
        public string ValidNameRegex { get; set; }

        [Option("no-cfg-deobfuscation", Default = false)]
        public bool NoCFGDeobfuscation { get; set; }

        [Option("no-string-decryption", Default = false)]
        public bool NoStringDecryption { get; set; }
        [Option("string-decrypt-method-token", HelpText = "Metadata token")]
        public string StringDecryptMethodToken { get; set; }
    }
}
