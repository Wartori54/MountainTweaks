using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Celeste.Mod.MountainTweaks;

public static class Util {
    public static void PrettyLogAllInstrs(StreamWriter builder, MethodBody il) {
        builder.WriteLine("In method " + il.Method.FullName);
        builder.WriteLine("Logging locals");
        PrintLocals(builder, il);

        builder.WriteLine("Logging instructions");
        RegenerateOffsets(il.Instructions);
        HashSet<Instruction> jmps = [];
        foreach (Instruction instr in il.Instructions) {
            switch (instr.Operand) {
                case Instruction jmp:
                    jmps.Add(jmp);
                    break;
                case Instruction[] jmpArr:
                    jmps.UnionWith(jmpArr);
                    break;
            }
        }

        foreach (Instruction instr in il.Instructions) {
            if (jmps.Contains(instr))
                builder.WriteLine($"JMP_{instr.Offset}:");
            builder.Write("    " + instr.OpCode.Name + ' ');
            switch (instr.OpCode.OperandType) {
                case OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget:
                    builder.WriteLine($"JMP_{(instr.Operand as Instruction)!.Offset}");
                    break;
                case OperandType.InlineSwitch:
                    builder.WriteLine(string.Join(", ", (instr.Operand as Instruction[])!.Select(i => $"JMP_{i}")));
                    break;
                case OperandType.InlineString:
                    builder.WriteLine($"\"{instr.Operand}\"");
                    break;
                default:
                    builder.WriteLine(instr.Operand);
                    break;
            }
        }
    }
        
    private static void RegenerateOffsets(Collection<Instruction> instrs) {
        int acc = 0;
        foreach (Instruction instr in instrs) {
            acc += instr.GetSize();
            instr.Offset = acc;
        }
    }
        
    private static void PrintLocals(TextWriter builder, MethodBody body) {
        for (int i = 0; i < body.Variables.Count; i++) {
            VariableDefinition varDef = body.Variables[i];
            builder.WriteLine($"[{i}] {varDef.VariableType.MetadataType.ToString()} [{varDef.VariableType.Scope.Name}]{varDef.VariableType.FullName} {varDef}");
        }
    }
}