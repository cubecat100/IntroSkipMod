using System.Reflection;
using System.Reflection.Emit;

var asm = typeof(MegaCrit.Sts2.Core.Modding.ModInitializerAttribute).Assembly;

if (args is ["--il", var typeName, var methodName])
{
    var type = asm.GetType(typeName) ?? throw new InvalidOperationException($"Type not found: {typeName}");
    var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .First(m => m.Name == methodName);
    DumpIl(method);
    return;
}

foreach (var type in asm.GetTypes()
             .Where(t => t.FullName?.Contains(args.Length > 0 ? args[0] : "MainMenu", StringComparison.OrdinalIgnoreCase) == true)
             .OrderBy(t => t.FullName))
{
    Console.WriteLine(type.FullName);
    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                 .OrderBy(m => m.Name))
    {
        var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
        Console.WriteLine($"  {method.Attributes} {method.ReturnType.FullName} {method.Name}({parameters})");
    }

    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                 .OrderBy(f => f.Name))
    {
        Console.WriteLine($"  FIELD {field.Attributes} {field.FieldType.FullName} {field.Name}");
    }

    foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                 .OrderBy(p => p.Name))
    {
        Console.WriteLine($"  PROP {prop.PropertyType.FullName} {prop.Name}");
    }
}

static void DumpIl(MethodBase method)
{
    Console.WriteLine($"{method.DeclaringType!.FullName}.{method.Name}");
    var body = method.GetMethodBody();
    if (body?.GetILAsByteArray() is not { } il)
    {
        Console.WriteLine("  <no body>");
        return;
    }

    var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);
    var module = method.Module;
    var pos = 0;

    while (pos < il.Length)
    {
        var offset = pos;
        ushort code = il[pos++];
        if (code == 0xFE)
        {
            code = (ushort)(0xFE00 | il[pos++]);
        }

        var op = opcodes[(short)code];
        object? operand = null;
        var operandText = "";

        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineBrTarget:
                operand = (sbyte)il[pos++];
                operandText = $"IL_{pos + (sbyte)operand:X4}";
                break;
            case OperandType.InlineBrTarget:
                operand = BitConverter.ToInt32(il, pos);
                pos += 4;
                operandText = $"IL_{pos + (int)operand:X4}";
                break;
            case OperandType.ShortInlineI:
                operandText = il[pos++].ToString();
                break;
            case OperandType.InlineI:
                operandText = BitConverter.ToInt32(il, pos).ToString();
                pos += 4;
                break;
            case OperandType.InlineI8:
                operandText = BitConverter.ToInt64(il, pos).ToString();
                pos += 8;
                break;
            case OperandType.ShortInlineR:
                operandText = BitConverter.ToSingle(il, pos).ToString();
                pos += 4;
                break;
            case OperandType.InlineR:
                operandText = BitConverter.ToDouble(il, pos).ToString();
                pos += 8;
                break;
            case OperandType.InlineString:
                operandText = "\"" + module.ResolveString(BitConverter.ToInt32(il, pos)) + "\"";
                pos += 4;
                break;
            case OperandType.InlineMethod:
                operandText = module.ResolveMethod(BitConverter.ToInt32(il, pos))?.ToString() ?? "";
                pos += 4;
                break;
            case OperandType.InlineField:
                operandText = module.ResolveField(BitConverter.ToInt32(il, pos))?.ToString() ?? "";
                pos += 4;
                break;
            case OperandType.InlineType:
                operandText = module.ResolveType(BitConverter.ToInt32(il, pos))?.ToString() ?? "";
                pos += 4;
                break;
            case OperandType.InlineTok:
                operandText = module.ResolveMember(BitConverter.ToInt32(il, pos))?.ToString() ?? "";
                pos += 4;
                break;
            case OperandType.InlineSig:
                operandText = $"sig:0x{BitConverter.ToInt32(il, pos):X8}";
                pos += 4;
                break;
            case OperandType.ShortInlineVar:
                operandText = il[pos++].ToString();
                break;
            case OperandType.InlineVar:
                operandText = BitConverter.ToUInt16(il, pos).ToString();
                pos += 2;
                break;
            case OperandType.InlineSwitch:
                var count = BitConverter.ToInt32(il, pos);
                pos += 4;
                var basePos = pos + count * 4;
                var targets = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    targets.Add($"IL_{basePos + BitConverter.ToInt32(il, pos):X4}");
                    pos += 4;
                }
                operandText = string.Join(", ", targets);
                break;
            default:
                operandText = op.OperandType.ToString();
                break;
        }

        Console.WriteLine($"  IL_{offset:X4}: {op.Name,-12} {operandText}");
    }
}
