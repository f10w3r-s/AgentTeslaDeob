using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace ILDeobfuscator
{
    class Program
    {
        static void Main(string[] args)
        {
            ParserResult<Options> result = Parser.Default.ParseArguments<Options>(args);
            result.WithParsed(Run);
            result.WithNotParsed(PrintError);
        }

        static void Run(Options opts)
        {
            try
            {
                ILDeobfuscator deobfuscator = new ILDeobfuscator(opts);
                deobfuscator.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}: {1}\n{2}", ex.GetType().ToString(), ex.Message, ex.StackTrace);
            }
        }

        static void PrintError(IEnumerable<Error> errs)
        {
            errs.Output();
        }
    }
}
