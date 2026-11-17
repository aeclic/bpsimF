// Read-only metadata/IL instrument. Compile with the Unity SDK's mcs; invoke with
// ASSEMBLY TYPE METHOD plus resolver directories. PASS: method offsets/tokens are
// printed without constructing or invoking any target object. No runtime parity
// claim. Invalidated by assembly/SDK changes. Reflection-only loading is deliberate.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
public static class RC2ManagedInspection
{
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender,eventArgs) => {
            string name=new AssemblyName(eventArgs.Name).Name;
            foreach(string directory in args.Skip(3)) { string file=Path.Combine(directory,name+".dll");if(File.Exists(file))return Assembly.ReflectionOnlyLoadFrom(file); }
            return Assembly.ReflectionOnlyLoad(eventArgs.Name);
        };
        Assembly assembly=Assembly.ReflectionOnlyLoadFrom(args[0]);Type type=assembly.GetType(args[1],true);
        foreach(FieldInfo field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))Console.WriteLine("FIELD "+field.Name+" "+field.FieldType+" public="+field.IsPublic+" attributes="+string.Join(",",field.GetCustomAttributesData().Select(a=>a.AttributeType.FullName)));
        MethodInfo method=type.GetMethod(args[2],BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        byte[] bytes=method.GetMethodBody().GetILAsByteArray();
        Dictionary<short,OpCode> codes=new Dictionary<short,OpCode>();foreach(FieldInfo f in typeof(OpCodes).GetFields())if(f.FieldType==typeof(OpCode)){OpCode op=(OpCode)f.GetValue(null);codes[op.Value]=op;}
        int offset=0;while(offset<bytes.Length){int start=offset;short code=bytes[offset++];if(code==0xfe)code=(short)(0xfe00|bytes[offset++]);OpCode op=codes[code];object value="";
            switch(op.OperandType){
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineBrTarget:value=offset+1+(sbyte)bytes[offset];offset++;break;
                case OperandType.ShortInlineI:value=(sbyte)bytes[offset++];break;
                case OperandType.ShortInlineVar:value=bytes[offset++];break;
                case OperandType.InlineVar:value=BitConverter.ToUInt16(bytes,offset);offset+=2;break;
                case OperandType.ShortInlineR:value=BitConverter.ToSingle(bytes,offset);offset+=4;break;
                case OperandType.InlineR:value=BitConverter.ToDouble(bytes,offset);offset+=8;break;
                case OperandType.InlineI8:value=BitConverter.ToInt64(bytes,offset);offset+=8;break;
                case OperandType.InlineSwitch:int count=BitConverter.ToInt32(bytes,offset);offset+=4;value="switch "+count;offset+=count*4;break;
                default:int token=BitConverter.ToInt32(bytes,offset);offset+=4;
                    try {if(op.OperandType==OperandType.InlineMethod)value=method.Module.ResolveMethod(token);else if(op.OperandType==OperandType.InlineField)value=method.Module.ResolveField(token);else if(op.OperandType==OperandType.InlineType)value=method.Module.ResolveType(token);else if(op.OperandType==OperandType.InlineString)value=method.Module.ResolveString(token);else if(op.OperandType==OperandType.InlineBrTarget)value=offset+token;else value=token;}catch{value=token;}break;
            }
            Console.WriteLine(start.ToString("x4")+" "+op.Name+" "+value);
        }
    }
}
