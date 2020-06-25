using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace ILDeobfuscator
{
    public class CFGDeobfuscator
        : IDeobfuscationPass
    {
        private static readonly Code[] SwitchProloguePattern =
        {
            Code.Ldc_I4,
            Code.Ldc_I4,
            Code.Xor,
            Code.Dup,
            Code.Stloc,
            Code.Ldc_I4,
            Code.Rem_Un,
            Code.Switch,
            Code.Br
        };

        private static readonly Code[] SwitchBlockPattern1 =
        {
            Code.Ldloc,
            Code.Ldc_I4,
            Code.Mul,
            Code.Ldc_I4,
            Code.Xor,
            Code.Br
        };

        private static readonly Code[] SwitchBlockPattern2 =
        {
            Code.Ldc_I4,
            Code.Br
        };

        private static readonly Code[] SwitchBlockPattern3 =
        {
            Code.Ldc_I4,
            Code.Dup,
            Code.Br,
            Code.Ldc_I4,
            Code.Dup,
            Code.Pop,
            Code.Br
        };

        private static readonly Code[] SwitchBlockPattern4 =
        {
            Code.Ldc_I4,
            Code.Dup,
            Code.Br,
            Code.Ldc_I4,
            Code.Dup,
            Code.Pop,
            Code.Ldloc,
            Code.Ldc_I4,
            Code.Mul,
            Code.Xor,
            Code.Br
        };

        private enum SwitchTargetType
        {
            Start,
            Type1,
            Type2,
            Type3,
            Type4,
            End
        }

        private struct SwitchTargetInfo
        {
            public SwitchTargetType Type;

            public int StartIndex;
            public int EndIndex;

            public int PatternIndex;

            public uint Constant1;
            public uint Constant2;
            public uint Constant3;
        }

        public void Run(ModuleDefMD module, Options opts)
        {
            if (opts.NoCFGDeobfuscation)
                return;

            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;

                    UnflattenMethod(method);
                }
            }
        }

        private void UnflattenMethod(MethodDef method)
        {
            try
            {
                method.Body.SimplifyMacros(method.Parameters);

                IList<int> switchPrologues = FindSwitchPrologues(method.Body.Instructions);
                if (switchPrologues.Count == 0)
                    return;

                foreach (int switchStartIndex in switchPrologues)
                    DeobfuscateSwitch(method, switchStartIndex);
            }
            finally
            {
                method.Body.OptimizeMacros();
            }
        }

        /*
            loc_8D86:
                ldc.i4   0xF967D444    ; switchStartIndex
            loc_8D8B:
                ldc.i4   0xE36F2C9D
                xor
                dup
                stloc    0x13
                ldc.i4   5
                rem.un
                switch   loc_8DED, loc_8E55, loc_8E2A, loc_8D86, loc_8DBA
                br       loc_8E55
         */
        private void DeobfuscateSwitch(MethodDef method, int switchStartIndex)
        {
            IList<Instruction> insns = method.Body.Instructions;

            uint initialState = (uint)(int)insns[switchStartIndex].Operand;
            uint stateConstant = (uint)(int)insns[switchStartIndex + 1].Operand;

            Instruction defaultBranch = (Instruction)insns[switchStartIndex + 8].Operand;
            int switchEndIndex = OffsetToIndex(insns, defaultBranch.Offset);

            List<int> switchTargets = ExtractTargetIndexes(insns, (IList<Instruction>)insns[switchStartIndex + 7].Operand);
            List<SwitchTargetInfo> switchTargetInfos = AnalyzeSwitchTargets(insns, switchStartIndex, switchEndIndex, switchTargets);

            Dictionary<int, List<int>> cfg = AnalyzeControlFlowGraph(initialState, stateConstant, switchStartIndex, switchEndIndex, switchTargetInfos);

            foreach (KeyValuePair<int, List<int>> item in cfg)
            {
                if (item.Key < 0)
                    continue;

                SwitchTargetInfo info = switchTargetInfos[item.Key];

                switch (info.Type)
                {
                    case SwitchTargetType.Type1:
                    case SwitchTargetType.Type2:
                        for (int i = info.PatternIndex; i <= info.EndIndex; i++)
                            insns[i].OpCode = OpCodes.Nop;

                        insns[info.PatternIndex].OpCode = OpCodes.Br;
                        insns[info.PatternIndex].Operand = insns[switchTargetInfos[item.Value[0]].StartIndex];
                        break;
                    case SwitchTargetType.Type3:
                    case SwitchTargetType.Type4:
                        for (int i = info.PatternIndex; i <= info.EndIndex; i++)
                            insns[i].OpCode = OpCodes.Nop;

                        insns[info.PatternIndex - 1].Operand = insns[switchTargetInfos[item.Value[0]].StartIndex];
                        insns[info.PatternIndex].OpCode = OpCodes.Br;
                        insns[info.PatternIndex].Operand = insns[switchTargetInfos[item.Value[1]].StartIndex];
                        break;
                    default:
                        throw new InvalidMethodException();
                }
            }

            for (int i = switchStartIndex; i < switchStartIndex + SwitchProloguePattern.Length; i++)
                insns[i].OpCode = OpCodes.Nop;

            int entryIndex = switchTargetInfos[cfg[-1][0]].StartIndex;
            insns[switchStartIndex].OpCode = OpCodes.Br;
            insns[switchStartIndex].Operand = insns[entryIndex];
        }

        private Dictionary<int, List<int>> AnalyzeControlFlowGraph(uint initialState, uint stateConstant, int switchStartIndex, int switchEndIndex, List<SwitchTargetInfo> switchTargetInfos)
        {
            Dictionary<int, List<int>> cfg = new Dictionary<int, List<int>>();
            SortedSet<int> visited = new SortedSet<int>();

            Queue<Tuple<int, uint>> queue = new Queue<Tuple<int, uint>>();
            queue.Enqueue(new Tuple<int, uint>(-1, initialState));

            int currentNode = -1;
            int nextNode = -1;

            uint state = 0;

            SwitchTargetInfo info;

            while (true)
            {
                if (currentNode < 0)
                {
                    if (queue.Count == 0)
                    {
                        if (visited.Count -1 == switchTargetInfos.Count - 2)
                            break;

                        for (int i = 0; i < switchTargetInfos.Count; i++)
                        {
                            if (visited.Contains(i))
                                continue;

                            info = switchTargetInfos[i];
                            if (info.Type == SwitchTargetType.Type2)
                                queue.Enqueue(new Tuple<int, uint>(i, info.Constant1));
                        }

                        continue;
                    }

                    Tuple<int, uint> nodeState = queue.Dequeue();
                    currentNode = nodeState.Item1;
                    state = nodeState.Item2;
                }

                visited.Add(currentNode);

                uint state2 = state ^ stateConstant;
                nextNode = (int)(state2 % switchTargetInfos.Count);

                if (!cfg.ContainsKey(currentNode))
                    cfg[currentNode] = new List<int>();
                cfg[currentNode].Add(nextNode);

                info = switchTargetInfos[nextNode];
                if (info.Type == SwitchTargetType.End)
                {
                    currentNode = -1;
                    continue;
                }

                if (visited.Contains(nextNode))
                {
                    currentNode = -1;
                    continue;
                }

                currentNode = nextNode;

                switch (info.Type)
                {
                    case SwitchTargetType.Type1:
                        state = (state2 * info.Constant1) ^ info.Constant2;
                        break;
                    case SwitchTargetType.Type2:
                        state = info.Constant1;
                        break;
                    case SwitchTargetType.Type3:
                        queue.Enqueue(new Tuple<int, uint>(currentNode, info.Constant1));
                        queue.Enqueue(new Tuple<int, uint>(currentNode, info.Constant2));
                        currentNode = -1;
                        break;
                    case SwitchTargetType.Type4:
                        queue.Enqueue(new Tuple<int, uint>(currentNode, (state2 * info.Constant3) ^ info.Constant1));
                        queue.Enqueue(new Tuple<int, uint>(currentNode, (state2 * info.Constant3) ^ info.Constant2));
                        currentNode = -1;
                        break;
                    default:
                        throw new InvalidMethodException();
                }
            }

            return cfg;
        }

        private List<SwitchTargetInfo> AnalyzeSwitchTargets(IList<Instruction> insns, int switchStartIndex, int switchEndIndex, List<int> switchTargets)
        {
            List<SwitchTargetInfo> switchTargetInfos = new List<SwitchTargetInfo>();

            foreach (int switchTargetIndex in switchTargets)
            {
                SwitchTargetInfo info = new SwitchTargetInfo();

                if (switchTargetIndex == switchStartIndex)
                {
                    info.Type = SwitchTargetType.Start;
                    switchTargetInfos.Add(info);
                    continue;
                }

                if (switchTargetIndex == switchEndIndex)
                {
                    info.Type = SwitchTargetType.End;
                    info.StartIndex = switchTargetIndex;
                    switchTargetInfos.Add(info);
                    continue;
                }

                int switchTargetEndIndex = FindSwitchBlockEnd(insns, switchTargetIndex, switchStartIndex);
                if (switchTargetEndIndex < 0)
                    throw new InvalidMethodException();

                info.StartIndex = switchTargetIndex;
                info.EndIndex = switchTargetEndIndex;

                if (MatchCodePattern(insns, info.EndIndex - SwitchBlockPattern1.Length + 1, SwitchBlockPattern1))
                {
                    info.Type = SwitchTargetType.Type1;
                    info.PatternIndex = info.EndIndex - SwitchBlockPattern1.Length + 1;
                    info.Constant1 = (uint)(int)insns[info.PatternIndex + 1].Operand;
                    info.Constant2 = (uint)(int)insns[info.PatternIndex + 3].Operand;
                }
                else if (MatchCodePattern(insns, info.EndIndex - SwitchBlockPattern2.Length + 1, SwitchBlockPattern2))
                {
                    info.Type = SwitchTargetType.Type2;
                    info.PatternIndex = info.EndIndex - SwitchBlockPattern2.Length + 1;
                    info.Constant1 = (uint)(int)insns[info.PatternIndex].Operand;
                }
                else if (MatchCodePattern(insns, info.EndIndex - SwitchBlockPattern3.Length + 1, SwitchBlockPattern3))
                {
                    info.Type = SwitchTargetType.Type3;
                    info.PatternIndex = info.EndIndex - SwitchBlockPattern3.Length + 1;
                    info.Constant1 = (uint)(int)insns[info.PatternIndex].Operand;
                    info.Constant2 = (uint)(int)insns[info.PatternIndex + 3].Operand;
                }
                else if (MatchCodePattern(insns, info.EndIndex - SwitchBlockPattern4.Length + 1, SwitchBlockPattern4))
                {
                    info.Type = SwitchTargetType.Type4;
                    info.PatternIndex = info.EndIndex - SwitchBlockPattern4.Length + 1;
                    info.Constant1 = (uint)(int)insns[info.PatternIndex].Operand;
                    info.Constant2 = (uint)(int)insns[info.PatternIndex + 3].Operand;
                    info.Constant3 = (uint)(int)insns[info.PatternIndex + 7].Operand;
                }
                else
                    throw new InvalidMethodException();

                switchTargetInfos.Add(info);
            }

            return switchTargetInfos;
        }

        private List<int> ExtractTargetIndexes(IList<Instruction> insns, IList<Instruction> targets)
        {
            List<int> indexes = new List<int>();

            foreach (Instruction insn in targets)
                indexes.Add(OffsetToIndex(insns, insn.Offset));

            return indexes;
        }

        public List<int> FindSwitchPrologues(IList<Instruction> insns)
        {
            List<int> switchPrologues = new List<int>();

            int index = 0;
            while ((index = FindCodePattern(insns, index, SwitchProloguePattern)) >= 0)
            {
                switchPrologues.Add(index);
                index++;
            }

            return switchPrologues;
        }

        /*
            loc_8D86:
                ldc.i4   0xF967D444 ; << prologueIndex

            loc_8D8B:               ; << prologueIndex + 1
                ldc.i4   0xE36F2C9D
                xor
                dup
                stloc    0x13
                ldc.i4   5
                rem.un
                switch   loc_8DED, loc_8E55, loc_8E2A, loc_8D86, loc_8DBA
                br       loc_8E55
            loc_8DED:
                [...]
                ldloc    0x13
                ldc.i4   0x85963B1D
                mul
                ldc.i4   0x3F5517BA
                xor
                br       loc_8D8B   ; << branch to prologueIndex + 1
         */
        private int FindSwitchBlockEnd(IList<Instruction> insns, int startIndex, int prologueIndex)
        {
            for (int i = startIndex; i < insns.Count; i++)
            {
                Instruction insn = insns[i];
                if (insn.OpCode.Code != Code.Br)
                    continue;

                Instruction operand = (Instruction)insn.Operand;
                if (OffsetToIndex(insns, operand.Offset) == prologueIndex + 1)
                    return i;
            }

            return -1;
        }

        private int OffsetToIndex(IList<Instruction> insns, uint offset)
        {
            for (int i = 0; i < insns.Count; i++)
            {
                if (insns[i].Offset == offset)
                    return i;
            }

            return -1;
        }

        private int FindCodePattern(IList<Instruction> insns, int startIndex, Code[] pattern)
        {
            for (int i = startIndex; i < insns.Count - pattern.Length; i++)
            {
                if (MatchCodePattern(insns, i, pattern))
                    return i;
            }

            return -1;
        }

        private bool MatchCodePattern(IList<Instruction> insns, int startIndex, Code[] pattern)
        {
            bool match = true;

            for (int i = 0; i < pattern.Length; i++)
            {
                if (insns[startIndex + i].OpCode.Code != pattern[i])
                {
                    match = false;
                    break;
                }
            }

            return match;
        }
    }
}
